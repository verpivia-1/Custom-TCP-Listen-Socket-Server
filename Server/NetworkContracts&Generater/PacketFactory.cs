using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.NetworkContracts_Generater
{
    public static class PacketFactory
    {
        public static IPacket Create(PacketId packetId)
        {
            return packetId switch
            {
                PacketId.C_CreateRoom => new C_CreateRoom(),
                PacketId.C_JoinRoom => new C_JoinRoom(),
                PacketId.S_RoomCreated => new S_RoomCreated(),
                PacketId.S_PlayerJoined => new S_PlayerJoined(),
                PacketId.S_PlayerLeft => new S_PlayerLeft(),
                PacketId.C_SelectThema => new C_SelectThema(),
                PacketId.S_ThemaSelected => new S_ThemaSelected(),
                PacketId.C_StartGame => new C_StartGame(),
                PacketId.S_GameStarted => new S_GameStarted(),
                PacketId.C_SelectCharacter  => new C_SelectCharacter(),
                PacketId.S_CharacterSelected => new S_CharacterSelected(),
                PacketId.C_RequestLobbySync => new C_RequestLobbySync(),

                PacketId.C_EnterNode => new C_EnterNode(),
                PacketId.S_NodeEntered => new S_NodeEntered(),
                PacketId.C_SuggestNode => new C_SuggestNode(),
                PacketId.S_NodeSuggested => new S_NodeSuggested(),

                PacketId.S_SeedBroadcast => new S_SeedBroadcast(),

                PacketId.S_ObjectSpawned    => new S_ObjectSpawned(),
                PacketId.S_ObjectDespawned  => new S_ObjectDespawned(),
                PacketId.C_NetworkVarUpdate => new C_NetworkVarUpdate(),
                PacketId.S_NetworkVarUpdate => new S_NetworkVarUpdate(),

                PacketId.S_ConnectionSuccess => new S_ConnectionSuccess(),
                PacketId.S_ConnectionFailed => new S_ConnectionFailed(),
                PacketId.S_ConnectionCancelled => new S_ConnectionCancelled(),
                _ => throw new NotFiniteNumberException()
            };

         }
    }
}