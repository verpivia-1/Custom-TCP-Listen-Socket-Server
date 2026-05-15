using Server.InGame.Networking;
using Server.NetworkContracts_Generater;
using System.Collections.Concurrent;

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

        RoomService(int roomId, int masterClientId, List<ClientSession> sessions, ClientSessionManager sessionManager)
        {
            RoomId = roomId;
            _masterClientId = masterClientId;
            _sessionManager = sessionManager;
            _sessions = new ConcurrentDictionary<int, ClientSession>(sessions.ToDictionary(s => s.ClientId));

            var registry = new NetworkPrefabRegistry();
            registry.Register(0, "Knight",  () => new NetworkBehaviour[] { new NetworkPlayerTransform(), new NetworkPlayerState(80f)  });
            registry.Register(1, "Veteran", () => new NetworkBehaviour[] { new NetworkPlayerTransform(), new NetworkPlayerState(120f) });

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
                case PacketId.C_NetworkVarUpdate:
                    HandleNetworkVarUpdate(session, (C_NetworkVarUpdate)packet); break;
                case PacketId.C_RequestLobbySync:
                    HandleRequestLobbySync(session); break;
                default:
                    break;
            }
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
            _enteredClients.Clear();

            // 스폰된 NetworkObject 전부 정리 (S_ObjectDespawned 브로드캐스트)
            foreach (var objId in _networkObjectManager.SpawnedObjects.Keys.ToList())
                _networkObjectManager.Despawn(objId);

            Broadcast(new S_BattleCleared { Column = packet.Column });
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
