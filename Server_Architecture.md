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
│   ├── RoomService.cs                 # 게임 내 전체 로직 처리
│   ├── MonsterFSM.cs                  # 몬스터 상태 머신
│   └── Networking/
│       ├── INetworkVariable.cs
│       ├── NetworkVariable.cs
│       ├── NetworkVariableSerializer.cs
│       ├── NetworkBehaviour.cs
│       ├── NetworkObject.cs
│       ├── NetworkObjectManager.cs
│       ├── NetworkPrefebRegister.cs
│       ├── NetworkPlayerTransform.cs
│       ├── NetworkPlayerState.cs
│       ├── NetworkMonsterTransform.cs
│       └── NetworkMonsterState.cs
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
│                             │    └─ NetworkObjectManager
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
- 방 생성 / 게스트 입장 처리
- 방 생성 즉시 RoomService 생성 (인원이 찰 때까지 기다리지 않음)
- 게임 시작 이벤트 수신 후 자신의 추적 데이터 정리 (이후 disconnect는 RoomService 단독 처리)

### 데이터 구조
```csharp
ConcurrentDictionary<int, Lobby>       _lobbies        // roomId → Lobby
ConcurrentDictionary<int, int>         _clientRoomMap  // clientId → roomId (역방향 조회)
ConcurrentDictionary<int, RoomService> _rooms          // roomId → RoomService
```

### 방 생성 흐름 (C_CreateRoom)
```
클라이언트가 제안한 RoomId 수신
  ├── 이미 방에 있으면 → S_RoomCreated { Success=false }
  ├── RoomId 중복이면 → S_RoomCreated { Success=false }
  └── 정상 → Lobby 생성, _lobbies / _clientRoomMap 등록
           → RoomService.TryCreate() → room.Start() 즉시 실행
           → room.OnGameStarted 구독 → CleanupRoom 등록
           → _rooms 등록
           → S_RoomCreated { Success=true, Lobby=LobbyInfo }
```

> 마스터 혼자서도 캐릭터 선택·로비 오브젝트 스폰이 가능하도록
> 방 생성 시점에 RoomService를 바로 만든다.

### 게스트 입장 흐름 (C_JoinRoom)
```
RoomId로 Lobby 조회
  ├── Lobby 없으면 → S_PlayerJoined { Success=false }
  ├── 만원이면     → S_PlayerJoined { Success=false }
  └── 정상 → lobby.TryAddPlayer(), _clientRoomMap 등록
           → room.AddSession(session)   ← 기존 RoomService에 세션 추가
           → S_PlayerJoined { Success=true, Lobby=LobbyInfo } 브로드캐스트
```

> 게스트는 이미 실행 중인 RoomService에 `AddSession`으로 합류한다.  
> 상태 동기화(캐릭터 선택 현황, 스폰 오브젝트)는 클라이언트가 `C_RequestLobbySync`를 보내 처리.

### 게임 시작 후 정리 (CleanupRoom)
```
room.OnGameStarted 이벤트
  → _lobbies / _clientRoomMap / _rooms에서 해당 방 전부 제거
```

이후 해당 클라이언트들의 disconnect는 RoomService가 단독으로 처리함.  
LobbyService는 `_clientRoomMap`에 없으면 disconnect를 무시하므로 자동으로 분리된다.

### 강제 연결 종료 처리 (OutGame 중)
```
ClientSessionManager.OnClientDisconnected 이벤트
  → _clientRoomMap에 없으면 (이미 게임 시작됨) → 무시
  → 있으면 → lobby.RemovePlayer()
           → 남은 인원 0명 → _lobbies / _rooms 삭제
           → 남은 인원 있음 → S_PlayerLeft 브로드캐스트
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
- 로비 / 인게임 전체 패킷 처리
- NetworkObjectManager 소유 (스폰/디스폰/변수 동기화)
- FlushLoop (30ms): 서버가 변경한 NetworkVariable을 브로드캐스트
- MonsterLoop (100ms): 몬스터 AI 틱 처리

### 내부 루프
```
FlushLoop (30ms)
  → NetworkObjectManager.FlushDirtyObjects()
  → dirty NetworkVariable → S_NetworkVarUpdate 브로드캐스트

MonsterLoop (100ms)
  → TickMonsters(dt)
       → 살아있는 몬스터마다 가장 가까운 플레이어 탐색
       → Chase 상태: Position.Value 이동 (dirty → FlushLoop가 전파)
       → FSM.Tick(dist, dt) → 상태 전환 / OnAttack 발행
```

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
| `C_RequestLobbySync` | 모두 | 현재 캐릭터 선택 현황 + 스폰 오브젝트 전체를 요청 클라이언트에 전송 |
| `C_GuestReady` | 게스트 | 마스터에게만 `S_GuestReady` 전달 |
| `C_SelectThema` | 마스터만 | `_selectedThemaId` 저장 → `S_ThemaSelected` 브로드캐스트 |
| `C_SelectCharacter` | 모두 | 로비 오브젝트 교체 스폰 → `S_CharacterSelected` 브로드캐스트 |
| `C_StartGame` | 마스터만, 최초 1회 | 로비 오브젝트 전부 Despawn → `S_SeedBroadcast` + `S_GameStarted` |
| `C_EnterNode` (Phase 1) | 마스터만 | `S_NodeEntered` (BattleSeed 생성) 브로드캐스트 |
| `C_EnterNode` (Phase 2) | 모두 | Game 씬 진입 완료 → 캐릭터 스폰 + 늦은 진입 동기화 |
| `C_SuggestNode` | 게스트 | 마스터에게만 `S_NodeSuggested` 전달 |
| `C_BattleClear` | 모두, 최초 1회 | 모든 오브젝트 Despawn → 상태 초기화 → `S_BattleCleared` 브로드캐스트 |
| `C_SpawnMonsters` | 마스터만, 최초 1회 | 몬스터 스폰 + FSM 생성 → `S_ObjectSpawned` 브로드캐스트 |
| `C_MonsterHit` | 모두 | 몬스터 HP 감소 → 사망 시 Despawn → `S_ObjectDespawned` |
| `C_NetworkVarUpdate` | Owner만 | 변수 갱신 → 발신자 제외 `S_NetworkVarUpdate` 중계 |

### C_EnterNode 2-Phase 설계

```
Phase 1 (마스터 → 서버, NodeSelect 씬에서):
  _nodeEntered = false → true
  S_NodeEntered 브로드캐스트 (씬 전환 신호)
  ※ Attrito/Apice 외 노드 타입은 Game 씬을 안 쓰므로 즉시 _nodeEntered = false 리셋

Phase 2 (각 클라이언트 → 서버, Game 씬 로드 완료 후):
  _enteredClients에 추가 (중복 방지)
  캐릭터 NetworkObject 스폰 → S_ObjectSpawned (해당 클라이언트에)
  이미 스폰된 타인 오브젝트 → S_ObjectSpawned (해당 클라이언트에만 전송)
```

### 연결 종료 처리 (InGame 중)
```
session.OnDisconnected 이벤트
  → _sessions에서 제거
  → 로비 스폰 오브젝트 Despawn (S_ObjectDespawned 브로드캐스트)
  → _characterSelections / _enteredClients에서 제거
  → 마스터가 나간 경우 새 마스터 선출
  → S_PlayerLeft 브로드캐스트
  → 남은 인원 0명 → Stop()
```

---

## 연결 종료 처리 두 경로 비교

| 상황 | 처리 주체 | 감지 방식 |
|---|---|---|
| 로비 대기 중 끊김 | `LobbyService` | `ClientSessionManager.OnClientDisconnected` 이벤트 |
| 게임 시작 후 끊김 | `RoomService` | `session.OnDisconnected`에 직접 구독 |

게임 시작 시 `CleanupRoom`이 `_clientRoomMap`에서 해당 클라이언트들을 제거하므로
LobbyService는 이후 disconnect를 자동으로 무시함.

---

## 전체 패킷 흐름 요약

```
OutGame (LobbyService)
  C → S   C_CreateRoom          방 생성 요청
  S → C   S_RoomCreated         방 생성 결과
  C → S   C_JoinRoom            게스트 입장 요청
  S → All S_PlayerJoined        입장 브로드캐스트
  S → All S_PlayerLeft          퇴장 브로드캐스트 (강제종료 시)

InGame (RoomService — 로비 단계)
  C → S   C_RequestLobbySync    게스트 진입 시 현재 상태 동기화 요청
  C → S   C_GuestReady          게스트 준비 토글
  S → M   S_GuestReady          게스트 준비 상태 마스터에 전달
  C → S   C_SelectThema         테마 선택 (마스터)
  S → All S_ThemaSelected
  C → S   C_SelectCharacter     캐릭터 선택
  S → All S_CharacterSelected   누가 어떤 캐릭터 골랐는지
  S → All S_ObjectSpawned       로비 프리뷰 오브젝트 스폰
  S → All S_ObjectDespawned     교체/게임 시작 시 로비 오브젝트 제거
  C → S   C_StartGame           게임 시작 (마스터)
  S → All S_SeedBroadcast       맵 시드
  S → All S_GameStarted         게임 시작 + 최종 참가자 목록

InGame (RoomService — NodeSelect 단계)
  C → S   C_EnterNode (Phase 1) 마스터가 노드 선택 확정
  S → All S_NodeEntered         씬 전환 + BattleSeed 브로드캐스트
  C → S   C_SuggestNode         게스트가 노드 추천
  S → M   S_NodeSuggested       마스터에게만 전달

InGame (RoomService — Game 씬 단계)
  C → S   C_EnterNode (Phase 2) 각 클라이언트 Game 씬 로드 완료 시 전송
  S → C   S_ObjectSpawned       본인 캐릭터 스폰 + 타인 오브젝트 동기화
  C → S   C_SpawnMonsters       마스터가 몬스터 스폰 위치 전송 (0x5001)
  S → All S_ObjectSpawned       몬스터 스폰 + 초기 위치/HP
  C → S   C_MonsterHit          플레이어 공격으로 몬스터 피격 보고 (0x5002)
  S → All S_ObjectDespawned     몬스터 사망 시 제거
  C → S   C_NetworkVarUpdate    Owner가 자신의 변수 갱신 (위치 등)
  S → All S_NetworkVarUpdate    변수 동기화 브로드캐스트 (FlushLoop / 중계)
  C → S   C_BattleClear         배틀 클리어 신호
  S → All S_BattleCleared       클리어 브로드캐스트 + 서버 상태 초기화
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
게임 시작 시 `CleanupRoom`으로 맵 제거 → LobbyService가 자동으로 해당 클라이언트 무시.

### 4. RoomService 패킷 필터링
전역 이벤트에서 `_sessions.ContainsKey(clientId)`로 자기 방 여부 확인.
여러 RoomService가 동시에 떠있어도 각자 자신의 클라이언트만 처리함.

### 5. RoomService 즉시 생성
마스터가 방을 만드는 즉시 RoomService가 시작됨.
게스트는 나중에 `AddSession`으로 합류. 게임 시작 전까지 인원 제한 없음.
→ 마스터 혼자 로비에서 캐릭터 선택·스폰이 가능함.

### 6. NetworkObject 시스템
RoomService가 `NetworkObjectManager`를 소유하며 스폰/디스폰/변수 동기화를 위임함.
자세한 구조는 `NetworkObject_Architecture.md` 참조.
