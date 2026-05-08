# NetworkObject 시스템 학습 자료

## 개요

서버 측 NetworkObject 시스템은 Unity NGO(Netcode for GameObjects)의 `NetworkObject` / `NetworkBehaviour` 구조를
서버에 그대로 미러링한 설계다.

클라이언트의 Unity 컴포넌트 구조와 개념적으로 1:1 대응되어,
"클라이언트의 어떤 변수가 어떤 패킷으로 오는가"를 직관적으로 추적할 수 있다.

---

## 전체 파일 구조

```
InGame/Networking/
  ├─ INetworkVariable.cs          인터페이스 정의
  ├─ NetworkVariable.cs           제네릭 변수 구현체
  ├─ NetworkVariableSerializer.cs 타입별 직렬화 헬퍼
  ├─ NetworkBehaviour.cs          추상 동기화 컴포넌트
  ├─ NetworkObject.cs             동기화 단위 오브젝트
  ├─ NetworkObjectManager.cs      스폰/디스폰/flush 관리자
  ├─ NetworkPrefebRegister.cs     프리팹 팩토리 등록소
  ├─ NetworkPlayerTransform.cs    플레이어 위치 Behaviour
  └─ NetworkPlayerState.cs        플레이어 상태 Behaviour
```

---

## 클래스 계층 구조

```
NetworkObject
  └─ NetworkBehaviour[]          (BehaviourIndex로 구분)
       └─ INetworkVariable[]     (VariableIndex로 구분)
            └─ NetworkVariable<T>
```

이 인덱스 구조는 패킷의 `NetworkVariableDelta`와 직접 대응된다:

```
NetworkVariableDelta
  ├─ ObjectId        → NetworkObject.NetworkObjectId
  ├─ BehaviourIndex  → NetworkObject.Behaviours[i]
  ├─ VariableIndex   → behaviour.NetworkVariables[j]
  └─ Payload         → variable.Serialize(writer)의 출력
```

---

## 각 클래스 역할

### NetworkObject

게임 내 동기화 단위 하나를 나타낸다. 플레이어 1명, 클리어 포인트 1개 등.

| 멤버 | 설명 |
|---|---|
| `NetworkObjectId` | 서버가 발급한 고유 ID. 클라이언트와 공유됨 |
| `OwnerClientId` | 이 오브젝트를 소유한 클라이언트 ID |
| `PrefabIndex` | 어떤 프리팹인지 (클라이언트가 어떤 오브젝트를 생성할지 결정) |
| `IsSpawned` | 스폰 완료 여부 플래그 |
| `Name` | 디버그용 이름 |
| `IsDirty` | 소속 Behaviour 중 하나라도 dirty면 true |
| `AddBehaviour<T>()` | Behaviour 등록 (자신을 NetworkObject로 설정) |
| `GetBehaviour<T>()` | 타입으로 Behaviour 조회 |
| `MarkClean()` | 모든 Behaviour의 dirty 해제 |
| `NotifySpawn()` | 스폰 시 모든 Behaviour에 콜백 전달 |
| `NotifyDespawn()` | 디스폰 시 모든 Behaviour에 콜백 전달 |
| `ToSpawnedObjectInfo()` | 현재 변수 상태를 패킷 페이로드로 직렬화 |

```csharp
// 직접 생성하지 않음 — NetworkObjectManager.Spawn()이 담당
public NetworkObject(string name, int networkObjectId, int prefabIndex, int ownerClientId = 0)
```

---

### NetworkBehaviour

동기화 로직의 단위. 하나의 NetworkObject에 여러 개 붙을 수 있다.
Unity의 `MonoBehaviour`처럼 상속해서 사용한다.

| 멤버 | 설명 |
|---|---|
| `NetworkObject` | 소속 오브젝트 참조 |
| `NetworkObjectId` | 편의용 위임 프로퍼티 |
| `OwnerClientId` | 편의용 위임 프로퍼티 |
| `IsDirty` | 소속 변수 중 하나라도 dirty면 true |
| `RegisterVariable<T>()` | 변수 생성 + 내부 리스트에 등록 |
| `MarkClean()` | 모든 변수 dirty 해제 |
| `OnNetworkSpawn()` | 스폰 시 호출되는 가상 메서드 |
| `OnNetworkDespawn()` | 디스폰 시 호출되는 가상 메서드 |

`RegisterVariable`은 반드시 생성자에서 호출해야 한다.
내부 리스트에 추가되는 순서가 곧 `VariableIndex`가 되기 때문에
순서가 바뀌면 클라이언트와 인덱스 불일치가 발생한다.

---

### INetworkVariable

변수 타입에 무관하게 Behaviour가 변수를 일괄 처리할 수 있게 해주는 인터페이스.

```csharp
public interface INetworkVariable
{
    bool IsDirty { get; }
    void MarkClean();
    void Serialize(BinaryWriter writer);
    void Deserialize(BinaryReader reader);
}
```

---

### NetworkVariable\<T\>

실제 값을 저장하는 제네릭 변수. `INetworkVariable` 구현체.

| 멤버 | 설명 |
|---|---|
| `Value` | getter/setter. set 시 값이 다르면 dirty + 이벤트 발행 |
| `IsDirty` | 값이 변경된 후 flush 전까지 true |
| `MarkClean()` | dirty 해제 (flush 후 호출) |
| `SetWithoutNotify(v)` | dirty/이벤트 없이 값만 설정 |
| `OnValueChanged` | `Action<T, T>` 이벤트 (before, after) |
| `implicit operator T` | `variable.Value` 없이 바로 T로 사용 가능 |

**`SetWithoutNotify`를 쓰지 않으면 생기는 문제:**
```
클라이언트가 C_NetworkVarUpdate 전송
  → 서버 Deserialize → Value = x → dirty = true
  → FlushLoop가 dirty 감지 → S_NetworkVarUpdate 브로드캐스트
  → 보낸 클라이언트에게도 되돌아옴 (루프)
```
`ApplyVariableUpdate`에서 `Deserialize` 직후 `MarkClean()`을 호출해 이를 방지한다.

---

### NetworkVariableSerializer\<T\>

`NetworkVariable<T>.Serialize/Deserialize`의 실제 구현을 담당하는 static 헬퍼.

**지원 타입:**

| 카테고리 | 타입 |
|---|---|
| 정수형 | `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` |
| 실수형 | `float`, `double` |
| 논리형 | `bool` |
| 구조체 | `Vector3` |

지원하지 않는 타입으로 `NetworkVariable<T>`를 만들면
**런타임에 `NotSupportedException`** 이 발생한다.

> `Quaternion`은 지원되지만 이 게임은 2D라 사용하지 않는다.
> 2D 회전은 스프라이트 flip으로 처리하므로 회전 동기화가 불필요하다.

**`Unsafe.As<T, TTarget>(ref v)` 사용 이유:**
```csharp
// 일반 방법 — boxing (힙 할당)
writer.Write((float)(object)value);

// Unsafe.As — blitting (힙 할당 없음)
writer.Write(Unsafe.As<T, float>(ref value));
```
static 생성자에서 T가 확정된 시점에 델리게이트를 세팅해두므로
이후 매 Serialize 호출에서는 분기 없이 델리게이트만 호출된다.

---

### NetworkPlayerTransform

플레이어 위치 동기화 Behaviour.

```csharp
internal class NetworkPlayerTransform : NetworkBehaviour
{
    public NetworkVariable<Vector3> Position;  // VariableIndex = 0

    public NetworkPlayerTransform()
    {
        Position = RegisterVariable<Vector3>(Vector3.Zero);
    }
}
```

> Rotation 없음 — 2D 게임이고 `PlayerMovement`가 `Rigidbody2D.linearVelocity`만
> 사용하며 회전값을 쓰지 않는다.

---

### NetworkPlayerState

플레이어 HP / 생사 상태 Behaviour.

```csharp
internal class NetworkPlayerState : NetworkBehaviour
{
    public NetworkVariable<float> Hp;     // VariableIndex = 0
    public NetworkVariable<float> HpMax;  // VariableIndex = 1
    public NetworkVariable<bool>  IsDead; // VariableIndex = 2

    readonly float _baseHp;

    public NetworkPlayerState(float baseHp)   // 캐릭터별 기본 HP 주입
    {
        _baseHp = baseHp;
        Hp    = RegisterVariable<float>();
        HpMax = RegisterVariable<float>();
        IsDead = RegisterVariable<bool>(false);
    }

    public override void OnNetworkSpawn()
    {
        HpMax.Value = _baseHp;  // 스폰 시점에 초기화
        Hp.Value    = _baseHp;
    }
}
```

**왜 생성자에서 `Hp.Value = baseHp`를 하지 않는가:**
생성자 시점에는 아직 `NetworkObject`에 붙기 전이라 스폰 상태가 아니다.
초기화는 `OnNetworkSpawn()`에서 해야 `NotifySpawn()` 이후 dirty가 잡혀
`S_ObjectSpawned`의 초기 상태에 정상 반영된다.

---

### NetworkPrefabRegistry

`PrefabIndex → Behaviour 생성 팩토리` 매핑 등록소.

```csharp
registry.Register(0, "Knight",  () => new NetworkBehaviour[]
{
    new NetworkPlayerTransform(),
    new NetworkPlayerState(80f)
});
registry.Register(1, "Veteran", () => new NetworkBehaviour[]
{
    new NetworkPlayerTransform(),
    new NetworkPlayerState(120f)
});
```

`PrefabIndex`는 Unity 클라이언트의 `DefaultNetworkPrefabs` 리스트 순서와 반드시 일치해야 한다.
순서가 어긋나면 클라이언트가 엉뚱한 프리팹을 스폰한다.

| Index | 이름 | base_hp | Unity 프리팹 경로 |
|---|---|---|---|
| 0 | Knight | 80 | `Assets/Resources/Prefabs/Player/Knight.prefab` |
| 1 | Veteran | 120 | `Assets/Resources/Prefabs/Player/Veteran.prefab` |

---

### NetworkObjectManager

스폰/디스폰/변수 동기화를 총괄하는 관리자.
`RoomService`가 소유하며 생성 시 브로드캐스트 델리게이트를 주입받는다.

```csharp
new NetworkObjectManager(registry, Broadcast, BroadcastExcept)
//                                 ↑ 전체 전송  ↑ 특정 클라이언트 제외 전송
```

| 메서드 | 설명 |
|---|---|
| `Spawn(clientId, prefabIndex)` | 오브젝트 생성 → `S_ObjectSpawned` 브로드캐스트 |
| `Despawn(networkObjectId)` | 오브젝트 제거 → `S_ObjectDespawned` 브로드캐스트 |
| `FlushDirtyObjects()` | dirty 변수 → `S_NetworkVarUpdate` 브로드캐스트 |
| `ApplyVariableUpdate(senderClientId, delta)` | 클라이언트 변수 수신 → 나머지에 중계 |

---

## Dirty 패턴 흐름

```
[서버가 변수 변경 — 예: HP 감소]
  state.Hp.Value -= damage
    → _isDirty = true

[FlushLoop — 30ms마다 RoomService에서 호출]
  NetworkObjectManager.FlushDirtyObjects()
    → dirty 변수 Serialize → S_NetworkVarUpdate 전체 브로드캐스트
    → obj.MarkClean()

[클라이언트가 변수 변경 — 예: 위치 이동]
  C_NetworkVarUpdate 수신
    → ApplyVariableUpdate(senderClientId, delta)
         → variable.Deserialize(reader)   ← dirty = true 세팅됨
         → variable.MarkClean()           ← 즉시 해제 (FlushLoop 재전송 방지)
         → S_NetworkVarUpdate 브로드캐스트 (발신자 제외)
```

---

## 캐릭터 선택 → 스폰 전체 흐름

```
[로비 씬 — RoomService 단계]

  클라이언트가 캐릭터 선택
    C_SelectCharacter (prefabIndex)
      → _characterSelections[clientId] = prefabIndex
      → S_CharacterSelected 전체 브로드캐스트

  마스터가 게임 시작
    C_StartGame
      → S_SeedBroadcast (맵 시드)
      → S_GameStarted (최종 참가자 목록 + 선택 캐릭터 포함)
      → 각 클라이언트마다 NetworkObjectManager.Spawn(clientId, prefabIndex)
           → NetworkPrefabRegistry에서 팩토리 조회
           → NetworkObject 생성 + Behaviour 부착
           → NotifySpawn() → OnNetworkSpawn() 호출 (HP 초기화)
           → S_ObjectSpawned 브로드캐스트
```

---

## 패킷 흐름 전체 요약

```
OutGame (LobbyService)
  C → S   C_CreateRoom          방 생성
  S → C   S_RoomCreated         결과
  C → S   C_JoinRoom            입장
  S → All S_PlayerJoined        입장 브로드캐스트
  S → All S_PlayerLeft          퇴장 브로드캐스트

InGame (RoomService)
  C → S   C_SelectThema         테마 선택 (마스터)
  S → All S_ThemaSelected
  C → S   C_SelectCharacter     캐릭터 선택 (Knight=0 / Veteran=1)
  S → All S_CharacterSelected   누가 어떤 캐릭터 골랐는지
  C → S   C_StartGame           게임 시작 (마스터)
  S → All S_SeedBroadcast       맵 시드
  S → All S_GameStarted         게임 시작 + 최종 참가자 목록

InGame (NetworkObjectManager)
  S → All S_ObjectSpawned       플레이어 스폰 + 초기 HP/위치
  S → All S_ObjectDespawned     오브젝트 제거
  C → S   C_NetworkVarUpdate    위치 갱신 (Owner → Server)
  S → All S_NetworkVarUpdate    변수 동기화 브로드캐스트
```
