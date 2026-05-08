using Server.InGame.Networking;
using Server.NetworkContracts_Generater;
using System.Collections.Concurrent;

namespace Server.InGame
{
    internal class RoomService
    {
        public int RoomId { get; }

        readonly ClientSessionManager _sessionManager;
        readonly ConcurrentDictionary<int, ClientSession> _sessions;
        readonly int _masterClientId;

        string _selectedThemaId = string.Empty;
        bool _gameStarted = false;

        readonly Dictionary<int, int> _characterSelections = new(); // clientId → prefabIndex
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
                case PacketId.C_NetworkVarUpdate:
                    HandleNetworkVarUpdate(session, (C_NetworkVarUpdate)packet); break;
                default:
                    break;
            }
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

            _characterSelections[session.ClientId] = packet.PrefabIndex;
            Broadcast(new S_CharacterSelected
            {
                ClientId   = session.ClientId,
                PrefabIndex = packet.PrefabIndex
            });
        }

        void HandleStartGame(ClientSession session, C_StartGame packet)
        {
            if (session.ClientId != _masterClientId) return;
            if (_gameStarted) return;

            _gameStarted = true;

            Broadcast(new S_SeedBroadcast { Seed = new Random().Next() });
            Broadcast(new S_GameStarted
            {
                ThemaId = _selectedThemaId,
                Lobby   = BuildLobbyInfo()
            });

            // 각 클라이언트의 선택 캐릭터로 NetworkObject 스폰
            foreach (var s in _sessions.Values)
            {
                if (!_characterSelections.TryGetValue(s.ClientId, out int prefabIndex)) continue;
                _networkObjectManager.Spawn(s.ClientId, prefabIndex);
            }
        }

        void HandleNetworkVarUpdate(ClientSession session, C_NetworkVarUpdate packet)
        {
            _networkObjectManager.ApplyVariableUpdate(session.ClientId, packet.Delta);
        }

        void OnDisconnected(int clientId)
        {
            _sessions.TryRemove(clientId, out _);
            Console.WriteLine($"[RoomService] Client {clientId} disconnected. Remaining: {_sessions.Count}");

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
