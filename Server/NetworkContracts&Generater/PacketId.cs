using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.NetworkContracts_Generater
{
    public enum PacketId : ushort
    {
        // Lobby
        C_CreateRoom = 0x0001,         // Client → Server
        C_JoinRoom = 0x0002,           // Client → Server
        S_RoomCreated = 0x0003,        // Server → Client (방 코드 포함)
        S_PlayerJoined = 0x0004,       // Server → All

        //Room
        S_PlayerLeft = 0x0005,         // Server → All
        C_SelectThema = 0x0006,        // Client => Server
        S_ThemaSelected = 0x0007,      // Server => Client
        C_StartGame = 0x0008,          // Client(Master) → Server
        S_GameStarted = 0x0009,        // Server → All
        C_SelectCharacter = 0x000A,    // Client → Server (캐릭터 선택)
        S_CharacterSelected = 0x000B,  // Server → All (누가 어떤 캐릭터 골랐는지)

        // NodeSelect
        C_EnterNode = 0x1001,          // Client(Master) → Server
        S_NodeEntered = 0x1002,        // Server → All (진입 확정 브로드캐스트)
        C_SuggestNode = 0x1003,        // Client(Guest) → Server
        S_NodeSuggested = 0x1004,      // Server → Client(Master) only

        // Game
        S_SeedBroadcast = 0x2001,      // Server → All

        // NetworkObject
        S_ObjectSpawned    = 0x4001,   // Server → All (오브젝트 스폰 + 초기 상태)
        S_ObjectDespawned  = 0x4002,   // Server → All (오브젝트 제거)
        C_NetworkVarUpdate = 0x4003,   // Client(Owner) → Server (변수 갱신 요청)
        S_NetworkVarUpdate = 0x4004,   // Server → All except sender (변수 갱신 브로드캐스트)

        // Connection
        S_ConnectionSuccess = 0x3001,  // Server => Client
        S_ConnectionFailed = 0x3002,   // Server => Client
        S_ConnectionCancelled = 0x3003,// Server => Client
    }
}