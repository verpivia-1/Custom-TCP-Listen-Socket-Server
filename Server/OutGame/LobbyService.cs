using Server.InGame;
using Server.NetworkContracts_Generater;
using System.Collections.Concurrent;

namespace Server.OutGame
{
    internal class LobbyService
    {
        readonly ClientSessionManager _sessionManager;
        readonly ConcurrentDictionary<int, Lobby> _lobbies = new();
        // clientId → 속한 roomId (빠른 역방향 조회)
        readonly ConcurrentDictionary<int, int> _clientRoomMap = new();
        readonly ConcurrentDictionary<int, RoomService> _rooms = new();

        public LobbyService(ClientSessionManager clientSessionManager) 
        {
            _sessionManager = clientSessionManager;
        }

        public void Start()
        {
            _sessionManager.OnPacketReceived += OnPacketReceived;
            _sessionManager.OnClientDisconnected += OnClientDisconnected;
        }
        void OnPacketReceived(int clientId, IPacket packet)
        {
            if (!_sessionManager.TryGetClientSession(clientId, out ClientSession session))
                return;

            switch (packet.PacketId)
            {
                case PacketId.C_CreateRoom:
                    HandleCreateRoom(session, (C_CreateRoom)packet);
                    break;
                case PacketId.C_JoinRoom:
                    HandleJoinRoom(session, (C_JoinRoom)packet);
                    break;
                default:
                    break;
            }
        }
        void HandleCreateRoom(ClientSession session, C_CreateRoom packet) // master client용 방 생성
        {
            if (_clientRoomMap.ContainsKey(session.ClientId))
            {
                Console.WriteLine($"[Lobby] Client {session.ClientId} → Master 요청 실패: 이미 방에 있음");
                session.Send(new S_RoomCreated { Success = false, ErrorMessage = "Already in a room." });
                return;
            }
            if (_lobbies.ContainsKey(packet.RoomId))
            {
                Console.WriteLine($"[Lobby] Client {session.ClientId} → Master 요청 실패: RoomId={packet.RoomId} 이미 존재");
                session.Send(new S_RoomCreated { Success = false, ErrorMessage = "Room ID already exists." });
                return;
            }

            var lobby = new Lobby(packet.RoomId, packet.MaxPlayers, session.ClientId);
            _lobbies[packet.RoomId] = lobby;
            _clientRoomMap[session.ClientId] = packet.RoomId;

            // 마스터 단독으로 즉시 RoomService 생성 → 게스트 없이도 캐릭터 선택·스폰 가능
            if (RoomService.TryCreate(packet.RoomId, session.ClientId, new List<ClientSession> { session }, _sessionManager, out RoomService room, out string error))
            {
                int roomId = packet.RoomId;
                room.OnGameStarted += () => CleanupRoom(roomId);
                room.Start();
                _rooms[packet.RoomId] = room;
            }
            else
            {
                Console.WriteLine($"[Lobby] RoomService 생성 실패: {error}");
            }

            Console.WriteLine($"[Lobby] Client {session.ClientId} → Master | RoomId={packet.RoomId} MaxPlayers={packet.MaxPlayers}");
            session.Send(new S_RoomCreated { Success = true, Lobby = lobby.ToLobbyInfo() });
        }

        void HandleJoinRoom(ClientSession session, C_JoinRoom packet) // Guest client용 방 참가
        {
            if (!_lobbies.TryGetValue(packet.RoomId, out Lobby lobby))
            {
                Console.WriteLine($"[Lobby] Client {session.ClientId} → Guest 요청 실패: RoomId={packet.RoomId} 없음");
                session.Send(new S_PlayerJoined { Success = false, ErrorMessage = "Room not found." });
                return;
            }
            if (!lobby.TryAddPlayer(session.ClientId))
            {
                Console.WriteLine($"[Lobby] Client {session.ClientId} → Guest 요청 실패: RoomId={packet.RoomId} 방 꽉 참");
                session.Send(new S_PlayerJoined { Success = false, ErrorMessage = "Room is full." });
                return;
            }

            _clientRoomMap[session.ClientId] = packet.RoomId;

            // 기존 RoomService에 게스트 세션 추가 (현재 상태 동기화 포함)
            if (_rooms.TryGetValue(packet.RoomId, out RoomService room))
                room.AddSession(session);

            Console.WriteLine($"[Lobby] Client {session.ClientId} → Guest  | RoomId={packet.RoomId} 현재인원={lobby.PlayerCount}/{lobby.MaxPlayers}");
            Broadcast(lobby, new S_PlayerJoined { Success = true, Lobby = lobby.ToLobbyInfo() });
        }

        void OnClientDisconnected(ClientSession session) // 클라이언트 접속 종료시 강제로 세션닫아버리기
        {
            if (!_clientRoomMap.TryRemove(session.ClientId, out int roomId)) return;
            if (!_lobbies.TryGetValue(roomId, out Lobby lobby)) return;

            int newMasterId = lobby.RemovePlayer(session.ClientId);
            if (newMasterId == -1)
            {
                _lobbies.TryRemove(roomId, out _);
                _rooms.TryRemove(roomId, out _);
                return;
            }

            Broadcast(lobby, new S_PlayerLeft
            {
                LeftClientId = session.ClientId,
                NewMasterClientId = newMasterId,
                Lobby = lobby.ToLobbyInfo()
            });
        }

        // 게임 시작 시 호출 — _lobbies·_clientRoomMap 정리로 이후 인게임 disconnect를 RoomService가 단독 처리
        void CleanupRoom(int roomId)
        {
            if (!_lobbies.TryRemove(roomId, out Lobby lobby)) return;
            foreach (int id in lobby.GetPlayerIds())
                _clientRoomMap.TryRemove(id, out _);
            Console.WriteLine($"[Lobby] Room {roomId} 게임 시작 — Lobby 해제");
        }

        void Broadcast(Lobby lobby, IPacket packet) // 클라이언트에게 동일 패킷 전송
        {
            foreach (int clientId in lobby.GetPlayerIds())
                _sessionManager.Send(clientId, packet);
        }
    }
}