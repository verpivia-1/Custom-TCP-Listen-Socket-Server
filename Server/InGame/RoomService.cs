using Server.InGame.Networking;
using Server.NetworkContracts_Generater;
using System.Collections.Concurrent;
using System.Numerics;

namespace Server.InGame
{
    internal class RoomService
    {
        public int RoomId { get; }
        public event Action OnGameStarted;

        readonly ClientSessionManager _sessionManager;
        readonly ConcurrentDictionary<int, ClientSession> _sessions;
        int _masterClientId;

        string _selectedThemaId = string.Empty;
        bool _gameStarted = false;

        readonly Dictionary<int, int> _characterSelections = new(); // clientId → prefabIndex
        readonly Dictionary<int, int> _lobbySpawnedObjects = new(); // clientId → networkObjectId (로비 스폰 추적)
        readonly HashSet<int> _enteredClients = new();              // Game 씬 진입 완료 clientId
        bool _nodeEntered = false;                                  // Phase 1 완료 플래그
        bool _battleCleared = false;                                // 배틀 클리어 중복 방지
        readonly HashSet<int> _battleClearedSenders = new();       // 클리어 신호를 보낸 clientId
        readonly NetworkObjectManager _networkObjectManager;
        CancellationTokenSource _flushCts = new();

        readonly ConcurrentDictionary<int, MonsterEntry> _monsters = new();
        bool _monsterSpawned = false;

        record MonsterEntry(
            NetworkObject Obj,
            MonsterFSM Fsm,
            NetworkMonsterTransform Transform,
            NetworkMonsterState MonsterState,
            float MoveSpeed,
            float AttackDamage
        );

        RoomService(int roomId, int masterClientId, List<ClientSession> sessions, ClientSessionManager sessionManager)
        {
            RoomId = roomId;
            _masterClientId = masterClientId;
            _sessionManager = sessionManager;
            _sessions = new ConcurrentDictionary<int, ClientSession>(sessions.ToDictionary(s => s.ClientId));

            var registry = new NetworkPrefabRegistry();
            registry.Register(0, "Knight",        () => new NetworkBehaviour[] { new NetworkPlayerTransform(), new NetworkPlayerState(80f)   });
            registry.Register(1, "Veteran",       () => new NetworkBehaviour[] { new NetworkPlayerTransform(), new NetworkPlayerState(120f)  });
            registry.Register(2, "DimensionShard",() => new NetworkBehaviour[] { new NetworkMonsterTransform(), new NetworkMonsterState(100f) });

            _networkObjectManager = new NetworkObjectManager(registry, Broadcast, BroadcastExcept);
        }

        public static bool TryCreate(
            int roomId,
            int masterClientId,
            List<ClientSession> sessions,
            ClientSessionManager sessionManager,
            out RoomService room,
            out string error)
        {
            if (sessions == null || sessions.Count == 0)
            {
                room = null;
                error = "No sessions provided.";
                return false;
            }

            room = new RoomService(roomId, masterClientId, sessions, sessionManager);
            error = string.Empty;
            return true;
        }

        public void Start()
        {
            _sessionManager.OnPacketReceived += OnPacketReceived;

            foreach (var session in _sessions.Values)
                session.OnDisconnected += OnDisconnected;

            _ = FlushLoop(_flushCts.Token);
            _ = MonsterLoop(_flushCts.Token);
        }

        // 게스트 참가 시 호출 — 세션 등록만 수행. 상태 동기화는 C_RequestLobbySync로 처리
        public void AddSession(ClientSession session)
        {
            _sessions[session.ClientId] = session;
            session.OnDisconnected += OnDisconnected;
            Console.WriteLine($"[RoomService] Session {session.ClientId} added. Total: {_sessions.Count}");
        }

        void Stop()
        {
            _flushCts.Cancel();
            _sessionManager.OnPacketReceived -= OnPacketReceived;

            foreach (var session in _sessions.Values)
                session.OnDisconnected -= OnDisconnected;
        }

        async Task FlushLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _networkObjectManager.FlushDirtyObjects();
                    await Task.Delay(30, ct);
                }
            }
            catch (TaskCanceledException) { }
        }

        void OnPacketReceived(int clientId, IPacket packet)
        {
            if (!_sessions.ContainsKey(clientId)) return;
            if (!_sessionManager.TryGetClientSession(clientId, out ClientSession session)) return;

            switch (packet.PacketId)
            {
                case PacketId.C_SelectThema:
                    HandleSelectThema(session, (C_SelectThema)packet); break;
                case PacketId.C_SelectCharacter:
                    HandleSelectCharacter(session, (C_SelectCharacter)packet); break;
                case PacketId.C_StartGame:
                    HandleStartGame(session, (C_StartGame)packet); break;
                case PacketId.C_EnterNode:
                    HandleEnterNode(session, (C_EnterNode)packet); break;
                case PacketId.C_SuggestNode:
                    HandleSuggestNode(session, (C_SuggestNode)packet); break;
                case PacketId.C_BattleClear:
                    HandleBattleClear(session, (C_BattleClear)packet); break;
                case PacketId.C_SpawnMonsters:
                    HandleSpawnMonsters(session, (C_SpawnMonsters)packet); break;
                case PacketId.C_MonsterHit:
                    HandleMonsterHit(session, (C_MonsterHit)packet); break;
                case PacketId.C_NetworkVarUpdate:
                    HandleNetworkVarUpdate(session, (C_NetworkVarUpdate)packet); break;
                case PacketId.C_GuestReady:
                    HandleGuestReady(session, (C_GuestReady)packet); break;
                case PacketId.C_RequestLobbySync:
                    HandleRequestLobbySync(session); break;
                default:
                    break;
            }
        }

        void HandleGuestReady(ClientSession session, C_GuestReady packet)
        {
            if (session.ClientId == _masterClientId) return;
            if (_sessions.TryGetValue(_masterClientId, out var masterSession))
                masterSession.Send(new S_GuestReady { IsReady = packet.IsReady });
        }

        void HandleRequestLobbySync(ClientSession session)
        {
            foreach (var (clientId, prefabIndex) in _characterSelections)
                session.Send(new S_CharacterSelected { ClientId = clientId, PrefabIndex = prefabIndex });

            foreach (var obj in _networkObjectManager.SpawnedObjects.Values)
                session.Send(new S_ObjectSpawned { ObjectInfo = obj.ToSpawnedObjectInfo() });
        }

        void HandleSelectThema(ClientSession session, C_SelectThema packet)
        {
            if (session.ClientId != _masterClientId) return;

            _selectedThemaId = packet.ThemaId;
            Broadcast(new S_ThemaSelected { ThemaId = packet.ThemaId });
        }

        void HandleSelectCharacter(ClientSession session, C_SelectCharacter packet)
        {
            if (_gameStarted) return;

            // 이전 선택 캐릭터 교체 시 despawn
            if (_lobbySpawnedObjects.TryGetValue(session.ClientId, out int oldObjId))
            {
                _networkObjectManager.Despawn(oldObjId);
                _lobbySpawnedObjects.Remove(session.ClientId);
            }

            _characterSelections[session.ClientId] = packet.PrefabIndex;

            var obj = _networkObjectManager.Spawn(session.ClientId, packet.PrefabIndex);
            if (obj != null)
                _lobbySpawnedObjects[session.ClientId] = obj.NetworkObjectId;

            Broadcast(new S_CharacterSelected
            {
                ClientId    = session.ClientId,
                PrefabIndex = packet.PrefabIndex
            });
        }

        void HandleStartGame(ClientSession session, C_StartGame packet)
        {
            if (session.ClientId != _masterClientId) return;
            if (_gameStarted) return;

            _gameStarted = true;
            OnGameStarted?.Invoke();

            // 로비 스폰 오브젝트 정리 후 씬 전환 (Game 씬에서 C_EnterNode로 재스폰)
            foreach (var objId in _lobbySpawnedObjects.Values.ToList())
                _networkObjectManager.Despawn(objId);
            _lobbySpawnedObjects.Clear();

            Broadcast(new S_SeedBroadcast { Seed = new Random().Next() });
            Broadcast(new S_GameStarted
            {
                ThemaId = _selectedThemaId,
                Lobby   = BuildLobbyInfo()
            });
        }

        void HandleEnterNode(ClientSession session, C_EnterNode packet)
        {
            if (!_nodeEntered)
            {
                // Phase 1: NodeSelect에서 마스터가 전송 → S_NodeEntered 브로드캐스트 (씬 전환만, 스폰 없음)
                if (session.ClientId != _masterClientId) return;
                _nodeEntered = true;
                // 이전 배틀 클리어 상태 초기화
                _battleCleared = false;
                _battleClearedSenders.Clear();
                uint battleSeed = (uint)new Random().Next();
                Broadcast(new S_NodeEntered
                {
                    StageId    = packet.StageId,
                    NodeType   = packet.NodeType,
                    BattleSeed = battleSeed,
                    Column     = packet.Column
                });
                // Attrito/Apice 이외의 노드(샵·이벤트 등)는 Game 씬 로드가 없어
                // Phase2 C_EnterNode(col=0)가 오지 않으므로 즉시 리셋
                if (packet.NodeType != "Attrito" && packet.NodeType != "Apice")
                    _nodeEntered = false;
                return;
            }

            // Phase 2: Game 씬에서 각 클라이언트가 전송 → 캐릭터 스폰 + 늦은 진입 동기화
            if (!_enteredClients.Add(session.ClientId)) return;

            if (_characterSelections.TryGetValue(session.ClientId, out int prefabIndex))
                _networkObjectManager.Spawn(session.ClientId, prefabIndex);

            foreach (var obj in _networkObjectManager.SpawnedObjects.Values)
            {
                if (obj.OwnerClientId == session.ClientId) continue;
                session.Send(new S_ObjectSpawned { ObjectInfo = obj.ToSpawnedObjectInfo() });
            }
        }

        void HandleSuggestNode(ClientSession session, C_SuggestNode packet)
        {
            if (session.ClientId == _masterClientId) return;
            if (_sessions.TryGetValue(_masterClientId, out var masterSession))
                masterSession.Send(new S_NodeSuggested { NodeType = packet.NodeType, Column = packet.Column });
        }

        void HandleBattleClear(ClientSession session, C_BattleClear packet)
        {
            if (!_nodeEntered) return;                                 // 전투 미시작 상태에서 수신 시 무시
            if (!_battleClearedSenders.Add(session.ClientId)) return; // 중복 전송 무시
            if (_battleCleared) return;                                // 이미 처리됨

            _battleCleared = true;
            _nodeEntered = false;
            _monsterSpawned = false;
            _enteredClients.Clear();
            _monsters.Clear();

            // 스폰된 NetworkObject 전부 정리 (S_ObjectDespawned 브로드캐스트)
            foreach (var objId in _networkObjectManager.SpawnedObjects.Keys.ToList())
                _networkObjectManager.Despawn(objId);

            Broadcast(new S_BattleCleared { Column = packet.Column });
        }

        void HandleSpawnMonsters(ClientSession session, C_SpawnMonsters packet)
        {
            if (session.ClientId != _masterClientId) return;
            if (!_nodeEntered) return;
            if (_monsterSpawned) return;
            _monsterSpawned = true;

            for (int i = 0; i < packet.PosX.Length; i++)
            {
                var pos = new Vector3(packet.PosX[i], packet.PosY[i], 0f);
                var obj = _networkObjectManager.Spawn(-1, packet.PrefabIndex, netObj =>
                {
                    if (netObj.Behaviours.Count > 0 && netObj.Behaviours[0] is NetworkMonsterTransform mt)
                        mt.Position.Value = pos;
                });
                if (obj == null) continue;

                var fsm             = new MonsterFSM(6.0f, 1.2f, 1.5f);
                var monsterTransform = (NetworkMonsterTransform)obj.Behaviours[0];
                var monsterState     = (NetworkMonsterState)obj.Behaviours[1];
                var entry = new MonsterEntry(obj, fsm, monsterTransform, monsterState, 3.0f, 10f);

                fsm.OnStateChanged += newState => monsterState.State.Value = (int)newState;
                fsm.OnAttack += () =>
                {
                    var players = CollectPlayerData();
                    if (players.Count == 0) return;
                    var (_, ps) = FindNearestPlayer(entry.Transform.Position.Value, players);
                    DealDamage(ps, entry.AttackDamage);
                };

                _monsters[obj.NetworkObjectId] = entry;
                Console.WriteLine($"[RoomService] Monster spawned: objId={obj.NetworkObjectId} pos={pos}");
            }
        }

        void HandleMonsterHit(ClientSession session, C_MonsterHit packet)
        {
            if (!_monsters.TryGetValue(packet.ObjectId, out var entry)) return;
            if (entry.Fsm.State == MonsterState.Dead) return;

            float newHp = MathF.Max(entry.MonsterState.Hp.Value - packet.Damage, 0f);
            entry.MonsterState.Hp.Value = newHp;

            if (newHp <= 0f)
            {
                entry.Fsm.Kill();
                _monsters.TryRemove(packet.ObjectId, out _);
                _networkObjectManager.Despawn(packet.ObjectId);
            }
        }

        async Task MonsterLoop(CancellationToken ct)
        {
            const float dt = 0.1f;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TickMonsters(dt);
                    await Task.Delay(100, ct);
                }
            }
            catch (TaskCanceledException) { }
        }

        void TickMonsters(float dt)
        {
            if (_monsters.IsEmpty) return;

            var players = CollectPlayerData();

            foreach (var entry in _monsters.Values)
            {
                if (entry.Fsm.State == MonsterState.Dead) continue;
                if (!_networkObjectManager.SpawnedObjects.ContainsKey(entry.Obj.NetworkObjectId)) continue;

                if (players.Count == 0)
                {
                    entry.Fsm.Tick(float.MaxValue, dt);
                    continue;
                }

                var monsterPos = entry.Transform.Position.Value;
                var (nearestPos, _) = FindNearestPlayer(monsterPos, players);
                float dist = Vector3.Distance(monsterPos, nearestPos);

                if (entry.Fsm.State == MonsterState.Chase)
                {
                    var dir = Vector3.Normalize(nearestPos - monsterPos);
                    if (!float.IsNaN(dir.X))
                        entry.Transform.Position.Value = monsterPos + dir * entry.MoveSpeed * dt;
                }

                entry.Fsm.Tick(dist, dt);
            }
        }

        List<(Vector3 pos, NetworkPlayerState state)> CollectPlayerData()
        {
            var result = new List<(Vector3, NetworkPlayerState)>();
            foreach (var obj in _networkObjectManager.SpawnedObjects.Values)
            {
                if (obj.OwnerClientId < 0) continue;
                if (obj.Behaviours.Count < 2) continue;
                if (obj.Behaviours[0] is NetworkPlayerTransform pt && obj.Behaviours[1] is NetworkPlayerState ps)
                {
                    if (!ps.IsDead.Value)
                        result.Add((pt.Position.Value, ps));
                }
            }
            return result;
        }

        (Vector3 pos, NetworkPlayerState state) FindNearestPlayer(Vector3 monsterPos, List<(Vector3 pos, NetworkPlayerState state)> players)
        {
            var nearest = players[0];
            float minDist = Vector3.Distance(monsterPos, nearest.pos);
            for (int i = 1; i < players.Count; i++)
            {
                float d = Vector3.Distance(monsterPos, players[i].pos);
                if (d < minDist) { minDist = d; nearest = players[i]; }
            }
            return nearest;
        }

        void DealDamage(NetworkPlayerState playerState, float damage)
        {
            if (playerState.IsDead.Value) return;
            float newHp = MathF.Max(playerState.Hp.Value - damage, 0f);
            playerState.Hp.Value = newHp;
            if (newHp <= 0f)
                playerState.IsDead.Value = true;
        }

        void HandleNetworkVarUpdate(ClientSession session, C_NetworkVarUpdate packet)
        {
            _networkObjectManager.ApplyVariableUpdate(session.ClientId, packet.Delta);
        }

        void OnDisconnected(int clientId)
        {
            _sessions.TryRemove(clientId, out _);
            Console.WriteLine($"[RoomService] Client {clientId} disconnected. Remaining: {_sessions.Count}");

            // 로비 스폰 오브젝트 despawn (S_ObjectDespawned 브로드캐스트)
            if (_lobbySpawnedObjects.TryGetValue(clientId, out int objId))
            {
                _networkObjectManager.Despawn(objId);
                _lobbySpawnedObjects.Remove(clientId);
            }

            _characterSelections.Remove(clientId);
            _enteredClients.Remove(clientId);

            if (_sessions.Count > 0)
            {
                int newMaster = _sessions.ContainsKey(_masterClientId)
                    ? -1
                    : _sessions.Keys.First();

                if (newMaster != -1)
                    _masterClientId = newMaster;

                Broadcast(new S_PlayerLeft
                {
                    LeftClientId      = clientId,
                    NewMasterClientId = newMaster,
                    Lobby             = BuildLobbyInfo()
                });
            }

            if (_sessions.Count == 0)
                Stop();
        }

        void Broadcast(IPacket packet)
        {
            foreach (var session in _sessions.Values.ToList())
                session.Send(packet);
        }

        void BroadcastExcept(int excludeClientId, IPacket packet)
        {
            foreach (var session in _sessions.Values.ToList())
                if (session.ClientId != excludeClientId)
                    session.Send(packet);
        }

        LobbyInfo BuildLobbyInfo()
        {
            return new LobbyInfo
            {
                RoomId      = RoomId,
                MaxPlayers  = _sessions.Count,
                PlayerCount = _sessions.Count,
                PlayerList  = _sessions.Keys.Select(id => new PlayerInfo
                {
                    ClientId   = id,
                    IsMaster   = id == _masterClientId,
                    Ready      = true,
                    PrefabIndex = _characterSelections.TryGetValue(id, out int pi) ? pi : -1
                }).ToList()
            };
        }
    }
}
