# Custom TCP Listen Socket Server

C# .NET 8 기반 멀티플레이 게임 전용 TCP 서버. 최대 100명 동시 접속, 포트 9000.

---

## 폴더 구조

```
Server/
├── Program.cs                         # 진입점, AcceptLoop
├── ClientSession.cs                   # TCP 연결 1개 관리, RecvLoop
├── ClientSessionManager.cs            # 전체 세션 관리 + 패킷 큐
├── NetworkContracts&Generater/
│   ├── IPacket.cs
│   ├── Packet.cs                      # 모든 패킷 클래스 정의
│   ├── PacketId.cs                    # 패킷 식별자 enum
│   └── PacketFactory.cs
├── OutGame/
│   ├── Lobby.cs                       # 방 대기 데이터 모델
│   └── LobbyService.cs                # 방 생성/입장 처리
├── InGame/
│   ├── RoomService.cs                 # 인게임 전체 로직
│   ├── MonsterFSM.cs                  # 몬스터 AI 상태 머신
│   └── Networking/
│       ├── NetworkVariable.cs
│       ├── NetworkBehaviour.cs
│       ├── NetworkObject.cs
│       ├── NetworkObjectManager.cs    # 스폰/디스폰/변수 동기화
│       ├── NetworkPlayerTransform.cs
│       ├── NetworkPlayerState.cs
│       ├── NetworkMonsterTransform.cs
│       └── NetworkMonsterState.cs
└── Utils/
    ├── PacketIO.cs                    # 직렬화/역직렬화
    └── IdGenerator.cs                 # 고유 ID 발급
```

---

## 레이어 구조

```
[ 클라이언트 ]
      ↕ TCP (port 9000)
┌──────────────────────────┐
│  Network Layer           │  PacketIO — 직렬화/역직렬화
│  ClientSession           │  RecvLoop, Send
├──────────────────────────┤
│  Session Layer           │  ClientSessionManager
│                          │  ConcurrentQueue + ProcessPacketQueue
│                          │  OnPacketReceived 이벤트
├──────────────────────────┤
│  Service Layer           │  LobbyService  (OutGame)
│                          │  RoomService   (InGame)
│                          │    └─ NetworkObjectManager
└──────────────────────────┘
```

---

## 패킷 포맷

```
[ 4 bytes : 길이 헤더 ] [ 2 bytes : PacketId ] [ N bytes : 데이터 ]
```

- `C_` 접두사: 클라이언트 → 서버
- `S_` 접두사: 서버 → 클라이언트
- 수신 시 `ReadExact`로 원하는 바이트를 완전히 읽은 뒤 역직렬화

---

## 핵심 설계 원칙

**1. 단일 스레드 패킷 처리**
RecvLoop는 클라이언트마다 별도 스레드에서 돌지만, 처리는 `ProcessPacketQueue` 단일 루프에서 30ms 간격으로 순차 실행. 서비스 레이어에서 멀티스레드를 고려할 필요 없음.

**2. 이벤트 구독으로 서비스 분리**
`ClientSessionManager`는 서비스의 존재를 모름. `OnPacketReceived` 이벤트를 통해 서비스가 스스로 구독/해제.

**3. NetworkObject 시스템**
Unity NGO의 `NetworkObject` / `NetworkBehaviour` 구조를 서버에 미러링. `NetworkVariable<T>`의 dirty 플래그를 FlushLoop(30ms)가 감지해 `S_NetworkVarUpdate`로 브로드캐스트.

**4. 서버 권위 (Server Authority)**
`OwnerClientId` 검증 후 릴레이. 몬스터 AI는 서버의 `MonsterLoop(100ms)`가 단독 처리.

---

## 주요 루프

| 루프 | 주기 | 역할 |
|---|---|---|
| ProcessPacketQueue | 30ms | 패킷 큐 소비 → 서비스 핸들러 호출 |
| FlushLoop | 30ms | dirty NetworkVariable → `S_NetworkVarUpdate` 브로드캐스트 |
| MonsterLoop | 100ms | 몬스터 Chase 이동 + FSM 상태 전환 + 공격 판정 |
