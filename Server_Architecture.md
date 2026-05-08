# 서버 아키텍처 학습 자료

## 폴더 구조

```
Server/
├── Program.cs                         # 진입점, AcceptLoop
├── ClientSession.cs                   # 클라이언트 1개의 TCP 연결 관리
├── ClientSessionManager.cs            # 전체 세션 관리 + 패킷 큐
├── NetworkContracts&Generater/
│   ├── IPacket.cs                     # 패킷 인터페이스
│   ├── Packet.cs                      # 모든 패킷 클래스 정의
│   ├── PacketId.cs                    # 패킷 식별자 enum
│   └── PacketFactory.cs               # PacketId → 인스턴스 생성
├── OutGame/
│   ├── Lobby.cs                       # 방 대기 데이터 모델
│   └── LobbyService.cs                # 방 생성/입장 처리
├── InGame/
│   └── RoomService.cs                 # 게임 내 로직 처리
└── Utils/
    ├── PacketIO.cs                    # 직렬화/역직렬화
    └── IdGenerator.cs                 # 고유 ID 발급/반납
```

---

## 레이어 구조

```
[ 클라이언트 ]
      ↕ TCP
┌─────────────────────────────┐
│  Network Layer              │  PacketIO (직렬화/역직렬화)
│  ClientSession              │  RecvLoop, Send
├─────────────────────────────┤
│  Session Layer              │  ClientSessionManager
│                             │  패킷 큐 (ConcurrentQueue)
│                             │  OnPacketReceived 이벤트
├─────────────────────────────┤
│  Service Layer              │  LobbyService (OutGame)
│                             │  RoomService  (InGame)
└─────────────────────────────┘
```

---

## 패킷 포맷

`PacketIO.cs`가 정의하는 바이트 구조:

```
[ 4 bytes : 길이 헤더 ] [ 2 bytes : PacketId ] [ N bytes : 데이터 ]
```

- **길이 헤더**: `PacketId + 데이터`의 총 바이트 수 (헤더 자신 4바이트 제외)
- **PacketId**: `ushort` (2바이트) enum
- **데이터**: 각 패킷 클래스의 `Serialize` / `Deserialize`가 처리

### 송신 흐름 (Send)
```
IPacket
  → MemoryStream에 [0][PacketId][Serialize()] 기록
  → 실제 길이 계산 후 앞 4바이트에 덮어쓰기
  → NetworkStream.Write()
```

### 수신 흐름 (Recv)
```
NetworkStream
  → 4바이트 읽어 길이 파악
  → 나머지 N바이트 ReadExact()로 정확히 읽기
  → PacketFactory.Create(packetId)로 인스턴스 생성
  → Deserialize()로 데이터 채우기
```

> `ReadExact`를 쓰는 이유: TCP는 스트림 기반이라 한 번의 Read가 전체 데이터를 보장하지 않음.
> 원하는 바이트 수가 다 올 때까지 반복해서 읽어야 함.

---

## 서버 시작 흐름

```
Program.Main()
  ├── ClientSessionManager 생성 (내부에서 ProcessPacketQueue 루프 시작)
  ├── LobbyService 생성 → Start() (OnPacketReceived 이벤트 구독)
  ├── TcpListener.Start() on port 9000
  └── await AcceptLoop()
            │
            ▼
    AcceptTcpClientAsync() 대기
            │ 클라이언트 접속
            ▼
    ClientSessionManager.TryAddClient()
      ├── IdGenerator.AssignId() → clientId 발급 (최대 100)
      ├── ClientSession 생성
      ├── S_ConnectionSuccess 전송 (AssignedClientId 포함)
      └── session.StartRecvLoop() → 별도 스레드에서 RecvLoop 시작
```

---

## 패킷 수신 ~ 처리 흐름

```
[별도 스레드] ClientSession.RecvLoop()
  │  PacketIO.Recv()로 패킷 수신
  │
  ▼
ClientSessionManager.QueuePacket()
  │  ConcurrentQueue에 (session, packet) 추가
  │
  ▼
[ProcessPacketQueue 루프 - Task.Delay(30)마다 폴링]
  │  큐에서 꺼내기 → OnPacketReceived 이벤트 발행
  │
  ▼
LobbyService.OnPacketReceived()  또는  RoomService.OnPacketReceived()
  (이벤트를 구독한 서비스들이 각자 처리)
```

> **ProcessPacketQueue는 30ms 간격 폴링 방식**  
> 큐가 빌 때까지 소비하고 → `await Task.Delay(30)` → 반복.  
> 즉 패킷 처리는 단일 스레드에서 순차 실행됨 (스레드 안전 보장).

---

## OutGame: LobbyService

### 책임
- 방 생성 / 입장 처리
- 인원이 다 차면 RoomService로 이관

### 데이터 구조
```csharp
ConcurrentDictionary<int, Lobby> _lobbies        // roomId → Lobby
ConcurrentDictionary<int, int>   _clientRoomMap  // clientId → roomId (역방향 조회)
```

### 방 생성 흐름 (C_CreateRoom)
```
클라이언트가 제안한 RoomId 수신
  ├── 이미 방에 있으면 → S_RoomCreated { Success=false }
  ├── RoomId 중복이면 → S_RoomCreated { Success=false }
  └── 정상 → Lobby 생성, _lobbies / _clientRoomMap 등록
           → S_RoomCreated { Success=true, Lobby=LobbyInfo }
```

### 방 입장 흐름 (C_JoinRoom)
```
RoomId로 Lobby 조회
  ├── Lobby 없으면 → S_PlayerJoined { Success=false }
  ├── 만원이면     → S_PlayerJoined { Success=false }
  └── 정상 → _clientRoomMap 등록
           → 방 전체에 S_PlayerJoined { Success=true, Lobby=LobbyInfo } 브로드캐스트
           → lobby.IsFull이면 → TransferToRoom()
```

### RoomService 이관 (TransferToRoom)
```
Lobby의 playerIds로 ClientSession 목록 수집
  → RoomService.TryCreate() 호출
  → _clientRoomMap에서 해당 클라이언트들 제거
  → _lobbies에서 해당 방 제거
  → room.Start()
```

이관 후 LobbyService는 해당 클라이언트들을 더 이상 모름.  
이후 패킷은 RoomService가 처리.

### 강제 연결 종료 처리 (OutGame 중)
```
ClientSessionManager.OnClientDisconnected 이벤트
  → _clientRoomMap에 없으면 (이미 RoomService로 이관됨) → 무시
  → 있으면 → Lobby에서 제거
           → 남은 인원 0명 → _lobbies 삭제
           → 남은 인원 있음 → 마스터 교체 후 S_PlayerLeft 브로드캐스트
```

---

## OutGame: Lobby

방 대기 상태의 데이터 모델. 직접 패킷을 보내지 않음.

| 메서드 | 설명 |
|---|---|
| `TryAddPlayer(clientId)` | 만원/중복 체크 후 추가 |
| `RemovePlayer(clientId)` | 제거 후 새 마스터 id 반환 (-1이면 빈 방) |
| `GetPlayerIds()` | 복사본 반환 (스레드 안전) |
| `ToLobbyInfo()` | 패킷 전송용 LobbyInfo 빌드 |

마스터 교체 규칙: 마스터가 나가면 `_playerIds[0]` (다음 입장자)이 마스터가 됨.

---

## InGame: RoomService

### 책임
- 로비 씬 내 게임 로직 처리 (테마 선택, 게임 시작, 노드 진입)
- 자신의 방 소속 클라이언트 패킷만 처리

### 패킷 필터링
```csharp
void OnPacketReceived(int clientId, IPacket packet)
{
    if (!_sessions.ContainsKey(clientId)) return; // 내 방 아니면 무시
    ...
}
```
`OnPacketReceived`는 전역 이벤트라 모든 패킷이 들어옴.  
`_sessions`에 없는 clientId는 즉시 무시.

### 처리 패킷 목록

| 패킷 | 조건 | 동작 |
|---|---|---|
| `C_SelectThema` | 마스터만 | `_selectedThemaId` 저장 → `S_ThemaSelected` 브로드캐스트 |
| `C_StartGame` | 마스터만, 최초 1회 | `S_SeedBroadcast` + `S_GameStarted` 브로드캐스트 |
| `C_EnterNode` | 마스터만 | `S_NodeEntered` (BattleSeed 생성) 브로드캐스트 |
| `C_SuggestNode` | 게스트 | 마스터에게만 `S_NodeSuggested` 전송 |

### 연결 종료 처리 (InGame 중)
```
session.OnDisconnected 이벤트 (각 세션에 직접 구독)
  → _sessions에서 제거
  → 남은 인원 0명 → Stop() (OnPacketReceived 이벤트 구독 해제)
```

---

## 연결 종료 처리 두 경로 비교

| 상황 | 처리 주체 | 방식 |
|---|---|---|
| 로비 대기 중 끊김 | `LobbyService` | `ClientSessionManager.OnClientDisconnected` 이벤트 |
| 게임 방 입장 후 끊김 | `RoomService` | `session.OnDisconnected`에 직접 구독 |

이관 시 `_clientRoomMap`에서 제거되므로 LobbyService는 자동으로 해당 클라이언트를 무시함.

---

## 전체 패킷 흐름 요약

```
OutGame (LobbyService)
  C → S  C_CreateRoom        방 생성 요청
  S → C  S_RoomCreated       방 생성 결과
  C → S  C_JoinRoom          방 입장 요청
  S → All S_PlayerJoined     입장 브로드캐스트
  S → All S_PlayerLeft       퇴장 브로드캐스트 (강제종료 시)

  ※ IsFull 조건 충족 시 → RoomService로 이관

InGame (RoomService)
  C → S  C_SelectThema       테마 선택 (마스터)
  S → All S_ThemaSelected    테마 브로드캐스트
  C → S  C_StartGame         게임 시작 (마스터)
  S → All S_SeedBroadcast    랜덤 시드 브로드캐스트
  S → All S_GameStarted      게임 시작 브로드캐스트
  C → S  C_EnterNode         노드 진입 (마스터)
  S → All S_NodeEntered      노드 진입 확정 브로드캐스트
  C → S  C_SuggestNode       노드 제안 (게스트)
  S → C  S_NodeSuggested     마스터에게만 전달
```

---

## 핵심 설계 원칙 정리

### 1. 패킷 큐 - 단일 스레드 처리
수신은 클라이언트마다 별도 스레드(`RecvLoop`)에서 동시에 일어나지만,
처리는 `ProcessPacketQueue`의 단일 루프에서 순차 실행됨.
→ 서비스 레이어의 핸들러는 멀티스레드를 고려하지 않아도 됨.

### 2. 이벤트 구독으로 서비스 분리
`ClientSessionManager`는 서비스의 존재를 모름.
`OnPacketReceived` 이벤트를 통해 서비스가 스스로 구독/해제함.
→ 서비스 추가 시 Manager 코드를 수정하지 않아도 됨.

### 3. 역방향 맵 (_clientRoomMap)
`clientId → roomId` 맵을 별도로 유지해 O(1) 조회.
이관 시 맵에서 제거 → LobbyService가 자동으로 해당 클라이언트 무시.

### 4. RoomService 패킷 필터링
전역 이벤트에서 `_sessions.ContainsKey(clientId)`로 자기 방 여부 확인.
여러 RoomService가 동시에 떠있어도 각자 자신의 클라이언트만 처리함.
