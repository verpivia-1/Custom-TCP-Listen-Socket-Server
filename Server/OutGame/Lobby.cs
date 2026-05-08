using Server.NetworkContracts_Generater;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.OutGame
{
    internal class Lobby
    {
        public int RoomId { get; }
        public int MaxPlayers { get; }
        public int MasterClientId { get; private set; }
        public string SelectedThemaId { get; private set; } = string.Empty;

        readonly List<int> _playerIds = new();
        readonly object _lock = new();

        public int PlayerCount => _playerIds.Count;
        public bool IsFull => _playerIds.Count >= MaxPlayers;

        public Lobby(int roomId, int maxPlayers, int masterClientId)
        {
            RoomId = roomId;
            MaxPlayers = maxPlayers;
            MasterClientId = masterClientId;
            _playerIds.Add(masterClientId);
        }

        public bool TryAddPlayer(int clientId)
        {
            lock (_lock)
            {
                if (IsFull || _playerIds.Contains(clientId)) return false;
                _playerIds.Add(clientId);
                return true;
            }
        }

        // 반환값: 새 마스터 id (-1이면 방 비어있음)
        public int RemovePlayer(int clientId)
        {
            lock (_lock)
            {
                _playerIds.Remove(clientId);
                if (_playerIds.Count == 0) return -1;

                if (MasterClientId == clientId)
                    MasterClientId = _playerIds[0]; // 다음 입장자가 마스터

                return MasterClientId;
            }
        }

        public void SetThema(string themaId) => SelectedThemaId = themaId;

        public IReadOnlyList<int> GetPlayerIds()
        {
            lock (_lock) return _playerIds.ToList();
        }

        public LobbyInfo ToLobbyInfo()
        {
            lock (_lock)
            {
                return new LobbyInfo
                {
                    RoomId = RoomId,
                    MaxPlayers = MaxPlayers,
                    PlayerCount = _playerIds.Count,
                    PlayerList = _playerIds.Select(id => new PlayerInfo
                    {
                        ClientId = id,
                        IsMaster = id == MasterClientId,
                        Ready = false
                    }).ToList()
                };
            }
        }
    }
}
