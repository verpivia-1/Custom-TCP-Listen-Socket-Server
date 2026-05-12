using Server.NetworkContracts_Generater;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.NetworkContracts_Generater
{
    public class LobbyInfo
    {
        public int RoomId { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public List<PlayerInfo> PlayerList { get; set; } = new();
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RoomId);
            writer.Write(PlayerCount);
            writer.Write(MaxPlayers);
            writer.Write(PlayerList.Count);
            foreach (var player in PlayerList)
            {
                player.Serialize(writer);
            }
        }
        public void Deserialize(BinaryReader reader)
        {
            RoomId = reader.ReadInt32();
            PlayerCount = reader.ReadInt32();
            MaxPlayers = reader.ReadInt32();
            {
                int count = reader.ReadInt32();
                PlayerList = new(count);
                for (int i = 0; i < count; i++)
                {
                    var item = new PlayerInfo();
                    item.Deserialize(reader);
                    PlayerList.Add(item);
                }

            }
        }
    }
    public class PlayerInfo
    {
        public int ClientId { get; set; }
        public bool IsMaster { get; set; }
        public bool Ready { get; set; }
        public int PrefabIndex { get; set; } = -1;  // -1 = 미선택
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ClientId);
            writer.Write(IsMaster);
            writer.Write(Ready);
            writer.Write(PrefabIndex);
        }
        public void Deserialize(BinaryReader reader)
        {
            ClientId = reader.ReadInt32();
            IsMaster = reader.ReadBoolean();
            Ready = reader.ReadBoolean();
            PrefabIndex = reader.ReadInt32();
        }
    }
    public class NetworkVariableDelta
    {
        public int ObjectId { get; set; }
        public int BehaviourIndex { get; set; }
        public int VariableIndex { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ObjectId);
            writer.Write(BehaviourIndex);
            writer.Write(VariableIndex);
            writer.Write(Payload.Length);
            writer.Write(Payload, 0, Payload.Length);
        }
        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadInt32();
            BehaviourIndex = reader.ReadInt32();
            VariableIndex = reader.ReadInt32();
            {
                int count = reader.ReadInt32();
                Payload = reader.ReadBytes(count);
            }
        }
    }
    public class SpawnedObjectInfo
    {
        public int ObjectId { get; set; }
        public int OwnerClientId { get; set; }
        public string ObjectName { get; set; } = string.Empty;
        public int PrefabIndex { get; set; }
        public List<NetworkVariableDelta> VariableDeltaList { get; set; } = new();
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ObjectId);
            writer.Write(OwnerClientId);
            writer.Write(ObjectName);
            writer.Write(PrefabIndex);
            writer.Write(VariableDeltaList.Count);
            foreach (var item in VariableDeltaList)
                item.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader)
        {
            ObjectId = reader.ReadInt32();
            OwnerClientId = reader.ReadInt32();
            ObjectName = reader.ReadString();
            PrefabIndex = reader.ReadInt32();
            {
                int count = reader.ReadInt32();
                VariableDeltaList = new(count);
                for (int i = 0; i < count; i++)
                {
                    var item = new NetworkVariableDelta();
                    item.Deserialize(reader);
                    VariableDeltaList.Add(item);
                }
            }
        }
    }

    #region
    public sealed class S_ConnectionSuccess : IPacket
    {
        public PacketId PacketId => PacketId.S_ConnectionSuccess;

        public int AssignedClientId { get; set; }
        public string Content { get; set; } = string.Empty;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(AssignedClientId);
            writer.Write(Content);
        }

        public void Deserialize(BinaryReader reader)
        {
            AssignedClientId = reader.ReadInt32();
            Content = reader.ReadString();
        }
    }
    public sealed class S_ConnectionFailed : IPacket
    {
        public PacketId PacketId => PacketId.S_ConnectionFailed;

        public string Failed_Respones { get; set; } = string.Empty;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Failed_Respones);
        }

        public void Deserialize(BinaryReader reader)
        {
            Failed_Respones = reader.ReadString();
        }

    }
    public sealed class S_ConnectionCancelled : IPacket
    {
        public PacketId PacketId => PacketId.S_ConnectionCancelled;

        public string Canceleed_Respones { get; set; } = string.Empty;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Canceleed_Respones);
        }

        public void Deserialize(BinaryReader reader)
        {
            Canceleed_Respones = reader.ReadString();
        }

    }
    #endregion

    #region
    public sealed class S_SeedBroadcast : IPacket
    {
        public PacketId PacketId => PacketId.S_SeedBroadcast;

        public int Seed { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Seed);
        }
        public void Deserialize(BinaryReader reader)
        {
            Seed = reader.ReadInt32();
        }
    }
    #endregion

    #region
    public sealed class C_EnterNode : IPacket
    {
        public PacketId PacketId => PacketId.C_EnterNode;

        public int StageId { get; set; }      // 현재 스테이지 (서버 검증용)
        public string NodeType { get; set; }  // "Frammento", "bottega" 등
        public int Column { get; set; }       // NodeClearTracker의 column과 동일

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(StageId);
            writer.Write(NodeType);
            writer.Write(Column);
        }
        public void Deserialize(BinaryReader reader)
        {
            StageId = reader.ReadInt32();
            NodeType = reader.ReadString();
            Column = reader.ReadInt32();
        }
    }
    public sealed class S_NodeEntered : IPacket
    {
        public PacketId PacketId => PacketId.S_NodeEntered;

        public int StageId { get; set; }
        public string NodeType { get; set; }
        public uint BattleSeed { get; set; }
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(StageId);
            writer.Write(NodeType);
            writer.Write(BattleSeed);
        }
        public void Deserialize(BinaryReader reader)
        {
            StageId = reader.ReadInt32();
            NodeType = reader.ReadString();
            BattleSeed = reader.ReadUInt32();
        }
    }
    public sealed class C_SuggestNode : IPacket
    {
        public PacketId PacketId => PacketId.C_SuggestNode;

        public string NodeType { get; set; }
        public int Column { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NodeType);
            writer.Write(Column);
        }
        public void Deserialize(BinaryReader reader)
        {
            NodeType = reader.ReadString();
            Column = reader.ReadInt32();
        }


    }
    public sealed class S_NodeSuggested : IPacket
    {
        public PacketId PacketId => PacketId.S_NodeSuggested;
        public string NodeType { get; set; }
        public int Column { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NodeType);
            writer.Write(Column);
        }

        public void Deserialize(BinaryReader reader)
        {
            NodeType = reader.ReadString();
            Column = reader.ReadInt32();
        }
    }
    #endregion

    #region Character Selection
    public sealed class C_SelectCharacter : IPacket
    {
        public PacketId PacketId => PacketId.C_SelectCharacter;
        public int PrefabIndex { get; set; }
        public void Serialize(BinaryWriter writer) => writer.Write(PrefabIndex);
        public void Deserialize(BinaryReader reader) => PrefabIndex = reader.ReadInt32();
    }
    public sealed class S_CharacterSelected : IPacket
    {
        public PacketId PacketId => PacketId.S_CharacterSelected;
        public int ClientId { get; set; }
        public int PrefabIndex { get; set; }
        public void Serialize(BinaryWriter writer) { writer.Write(ClientId); writer.Write(PrefabIndex); }
        public void Deserialize(BinaryReader reader) { ClientId = reader.ReadInt32(); PrefabIndex = reader.ReadInt32(); }
    }
    #endregion

    #region NetworkObject
    public sealed class S_ObjectSpawned : IPacket
    {
        public PacketId PacketId => PacketId.S_ObjectSpawned;
        public SpawnedObjectInfo ObjectInfo { get; set; } = new();
        public void Serialize(BinaryWriter writer) => ObjectInfo.Serialize(writer);
        public void Deserialize(BinaryReader reader) => ObjectInfo.Deserialize(reader);
    }
    public sealed class S_ObjectDespawned : IPacket
    {
        public PacketId PacketId => PacketId.S_ObjectDespawned;
        public int ObjectId { get; set; }
        public void Serialize(BinaryWriter writer) => writer.Write(ObjectId);
        public void Deserialize(BinaryReader reader) => ObjectId = reader.ReadInt32();
    }
    public sealed class C_NetworkVarUpdate : IPacket
    {
        public PacketId PacketId => PacketId.C_NetworkVarUpdate;
        public NetworkVariableDelta Delta { get; set; } = new();
        public void Serialize(BinaryWriter writer) => Delta.Serialize(writer);
        public void Deserialize(BinaryReader reader) => Delta.Deserialize(reader);
    }
    public sealed class S_NetworkVarUpdate : IPacket
    {
        public PacketId PacketId => PacketId.S_NetworkVarUpdate;
        public NetworkVariableDelta Delta { get; set; } = new();
        public void Serialize(BinaryWriter writer) => Delta.Serialize(writer);
        public void Deserialize(BinaryReader reader) => Delta.Deserialize(reader);
    }
    #endregion

    #region
    public sealed class C_CreateRoom : IPacket
    {
        public PacketId PacketId => PacketId.C_CreateRoom;
        public int RoomId { get; set; }
        public int MaxPlayers { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RoomId);
            writer.Write(MaxPlayers);
        }
        public void Deserialize(BinaryReader reader)
        {
            RoomId = reader.ReadInt32();
            MaxPlayers = reader.ReadInt32();
        }
    }
    public sealed class C_JoinRoom : IPacket
    {
        public PacketId PacketId => PacketId.C_JoinRoom;
        public int RoomId { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RoomId);
        }
        public void Deserialize(BinaryReader reader)
        {
            RoomId = reader.ReadInt32();
        }

    }
    public sealed class S_RoomCreated : IPacket
    {
        public PacketId PacketId => PacketId.S_RoomCreated;
        public bool Success { get; set; }       // 성공 여부
        public string? ErrorMessage { get; set; } // (코드 중복, 범위 오류 등)
        public LobbyInfo? Lobby { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Success);
            writer.Write(ErrorMessage ?? string.Empty);
            writer.Write(Lobby != null);
            Lobby?.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader)
        {
            Success = reader.ReadBoolean();
            ErrorMessage = reader.ReadString();

            if (reader.ReadBoolean())
            {
                Lobby = new LobbyInfo();
                Lobby.Deserialize(reader);
            }
        }
    }
    public sealed class S_PlayerJoined : IPacket
    {
        public PacketId PacketId => PacketId.S_PlayerJoined;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public LobbyInfo? Lobby { get; set; }
     
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Success);
            writer.Write(ErrorMessage ?? string.Empty);
            writer.Write(Lobby != null);
            Lobby?.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader)
        { 
            Success = reader.ReadBoolean();
            ErrorMessage = reader.ReadString();

            if (reader.ReadBoolean())
            {
                Lobby = new LobbyInfo();
                Lobby.Deserialize(reader);
            }
        }
    }
    public sealed class S_PlayerLeft : IPacket
    {
        public PacketId PacketId => PacketId.S_PlayerLeft;
        public int LeftClientId { get; set; }
        public int NewMasterClientId { get; set; }  // 변동 없으면 -1
        public LobbyInfo? Lobby { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(LeftClientId);
            writer.Write(NewMasterClientId);
            writer.Write(Lobby != null);
            Lobby?.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader)
        {
            LeftClientId= reader.ReadInt32();
            NewMasterClientId= reader.ReadInt32();
            if (reader.ReadBoolean())           
            {
                Lobby = new LobbyInfo();
                Lobby?.Deserialize(reader);
            }
        }
    }
    public sealed class C_SelectThema : IPacket
    {
        public PacketId PacketId => PacketId.C_SelectThema;
        public string ThemaId { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer .Write(ThemaId);
        }
        public void Deserialize(BinaryReader reader)
        {
            ThemaId= reader.ReadString();
        }
        
    }
    public sealed class S_ThemaSelected : IPacket
    {
        public PacketId PacketId => PacketId.S_ThemaSelected;
        public string ThemaId { get; set; }    // 클라이언트가 ThemaRepository.Load()로 로컬 조회

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ThemaId);
        }
        public void Deserialize(BinaryReader reader)
        {
            ThemaId = reader.ReadString();
        }
    }
    public sealed class C_StartGame : IPacket
    {
        public PacketId PacketId => PacketId.C_StartGame;

        public void Serialize(BinaryWriter writer)
        {
        }
        public void Deserialize(BinaryReader reader)
        {
        }
    }
    public sealed class C_RequestLobbySync : IPacket
    {
        public PacketId PacketId => PacketId.C_RequestLobbySync;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
    public sealed class S_GameStarted : IPacket
    {
        public PacketId PacketId => PacketId.S_GameStarted;
        public string ThemaId { get; set; }
        public LobbyInfo? Lobby { get; set; }  // 최종 확정 참가자 목록

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(ThemaId);
            writer.Write(Lobby != null);
            Lobby?.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader)
        {
            ThemaId = reader.ReadString();
            if (reader.ReadBoolean())
            {
                Lobby = new LobbyInfo();
                Lobby.Deserialize(reader);
            }
        }
    }
    #endregion
}