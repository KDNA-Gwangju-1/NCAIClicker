# 타격 대상 (크리처)

> 관련 이슈: #16, #17, #141, #148, #161 · 최종 수정: 2026-09-18

**이 문서는 로그다.** 이 기능을 고칠 때마다 갱신한다. 새 문서를 만들지 않는다.

## 무엇을 하는가

커서가 조준하는 대상의 **내구도와 피격**, 그리고 책상 평면 2축(XZ) **배회 이동·FSM 상태 머신·스폰 관리**를 담당한다.
타격은 내구도만 깎고, 다 깎이는 순간 `OnTargetBroken` 을 한 번 발행해 보상을 넘긴다.
평상시 크리처는 멈춰 서서 대기(Idle)와 짧은 배회(Moving)를 반복하며 책상 안전 경계 안을 배회한다.
피격 시에는 타격 지점 반대 방향으로 도망(Fleeing)치며, 연속 타격이 누적될수록 공포(패닉)가 누적되어 도망 속도가 가속된다.
파괴된 자리는 일정 시간 뒤 다시 스폰된다.

4종(일반·거치·고속·회복)은 **같은 스크립트에 `targets.csv` 의 다른 행**을 물린 것이다.
종류마다 클래스를 만들지 않는다.

파괴 연출은 작업 6.3 이다. 이 문서는 그때 함께 갱신한다.

## 왜 이 방법인가

| 검토한 방법 | 채택 | 이유 |
|---|---|---|
| 한 스크립트 + `targets.csv` 행으로 4종 구분 | ✅ | 4종의 차이는 수치뿐이다. 종류별 클래스를 만들면 CSV 를 고칠 때 코드도 같이 고쳐야 한다 |
| 종류별 파생 클래스 (`NormalTarget` 등) | ❌ | 상속 계층만 늘고 얻는 게 없다. 회복형도 `stamina_restore` 가 0 보다 큰 것뿐이다 |
| 피격 반경을 메시 크기에서 잰다 | ❌ | **처음에 이렇게 만들었다가 되돌렸다.** 작업 6.6 에서 모델을 갈아끼우면 판정 크기가 따라 변해 밸런스가 흔들린다 (ASSET_PIPELINE 1절) |
| 피격 반경을 루트의 고정값 × CSV 배율로 둔다 | ✅ | 모델과 무관하다. 확대 비율만 `economy.csv` 에서 읽는다 |
| 내구도를 `int` 로 둔다 | ❌ | 자동 망치 1회 피해가 0.6 이라 정수로 깎으면 버려진다 |
| 내구도를 `float` 로 둔다 | ✅ | 작은 피해도 누적된다 (ARCHITECTURE 코인 계산·정산 계약 1번) |
| 파괴 보상을 남은 내구도로 계산 | ❌ | 계약 2번이 **초기 최대 내구도** 기준이라고 못박았다. 남은 값으로 하면 마지막 타격 크기에 따라 보상이 달라진다 |
| FSM 대신 행동 트리(BT) 사용 | ❌ | 타격 대상의 상태는 Idle, Moving, BeingHit, Fleeing 의 단순 순환뿐이라 FSM으로 충분하다 (ARCHITECTURE 4절) |
| 이동과 스폰 수치를 코드 상수로 하드코딩 | ❌ | 밸런스 CSV 및 저금통 수집벽 업그레이드로 수치가 변하므로 BalanceData와 업그레이드 연동 구조로 계산한다 |
| Edit Mode 에서 DestroyImmediate 로 분기 (SafeDestroy) | ✅ | 에디트 모드 검증 하네스에서 Destroy 호출 시 오류 발생 및 씬 잔류를 방지하고 리스폰 회계를 온전히 검증한다 (#161) |
| 검증 하네스 쪽에서만 스텁을 우회 파괴 | ❌ | CreatureManager 의 RemoveDeadCreatures 실제 동작과 쿨다운 예약 회계가 온전히 검증되지 않는다 |

## 구조

```mermaid
flowchart LR
  subgraph Core["코어 플레이"]
    mgr["CreatureManager<br/>스폰 · 리스폰 · 개체수 관리"]
    movement["CreatureMovement<br/>평면 2축 이동 · FSM 제어"]
    target["Target<br/>내구도 · 피격 판정"]
    swing["호버 스윙 · 자동 망치<br/>(작업 2.3 · 3.2)"]
  end

  subgraph Growth["성장·저장"]
    econ["EconomyManager<br/>보상 지급"]
  end

  subgraph Fx["UI·연출"]
    vfx["파괴 연출 · 숫자 팝업<br/>(작업 6.3)"]
  end

  events{{"GameEvents"}}
  balance[("BalanceData<br/>targets.csv · stages.csv · economy.csv")]

  swing -- "OnHit(HitInfo)" --> target
  target -- "피격 통지" --> movement
  balance -. "SerializeField" .-> target
  balance -. "SerializeField" .-> movement
  balance -. "SerializeField" .-> mgr
  mgr -- "Instantiate & Initialize" --> target
  target == "OnTargetBroken 발행" ==> events
  events == "구독 (리스폰 타이머)" ==> mgr
  events == "구독" ==> econ
  events == "구독" ==> vfx
```

프리팹은 **3단**이다. 이 구조가 작업 6.6 에셋 교체를 코어 담당자의 작업과 분리한다.

```
TargetNormal (루트)          ← 로직: Target, CreatureMovement, SphereCollider
└─ Visual (빈 GameObject)    ← 연출이 스쿼시·스트레치로 여기를 스케일한다
   └─ Mesh                   ← 그레이박스 프리미티브. 교체는 이것만 갈아끼운다
```

| 클래스 | 경로 | 하는 일 |
|---|---|---|
| `Target` | `Assets/Scripts/Runtime/Targets/Target.cs` | `IHittable` 구현. 내구도, 피격, 파괴 1회 발행, 피격 반경 적용 |
| `CreatureState` | `Assets/Scripts/Runtime/Targets/CreatureState.cs` | 크리처 FSM 상태 열거형 (Idle, Moving, BeingHit, Fleeing) |
| `CreatureMovement` | `Assets/Scripts/Runtime/Targets/CreatureMovement.cs` | 평면 2축(XZ) 배회 이동, FSM 전이, 책상 평면 안전 경계 이탈 방지 |
| `CreatureManager` | `Assets/Scripts/Runtime/Core/CreatureManager.cs` | 크리처 4종 스폰, 동시 출현 수 유지, 파괴 후 리스폰 관리 |
| `CreatureHpDisplay` | `Assets/Scripts/Runtime/Targets/CreatureHpDisplay.cs` | 크리처 머리 위 실시간 HP 숫자 표시 및 피격 시 펀치 스케일 연출 |
| `DamagePopup` | `Assets/Scripts/Runtime/Targets/DamagePopup.cs` | 타격 시 피해량을 공중에 띄우고 서서히 페이드아웃 후 소멸하는 연출 |
| `TargetChecks` | `Assets/Scripts/Editor/TargetChecks.cs` | 프리팹 구조·동작 검증 25건 |
| `CreatureMovementChecks` | `Assets/Scripts/Editor/CreatureMovementChecks.cs` | 이동, FSM 전이, 경계 클램프, 스폰, HP표시 검증 13건 |

프리팹 4종은 `Assets/Prefabs/Targets/`, 머티리얼 4종은 `Assets/Materials/` 다.

| 프리팹 | `_targetId` | 그레이박스 | 색 |
|---|---|---|---|
| `TargetNormal` | `normal` | Cube | 청회색 |
| `TargetAnchor` | `anchor` | Cylinder | 갈색 |
| `TargetRunner` | `runner` | Sphere | 노랑 |
| `TargetTourist` | `tourist` | Capsule | 초록 |

높이는 4종 모두 **0.4 유닛**이고 바닥이 `y=0` 에 닿는다. 기준은 [ASSET_PIPELINE](../ASSET_PIPELINE.md) 2절.

### 이벤트

| 이벤트 | 발행/구독 | 언제 |
|---|---|---|
| `GameEvents.OnTargetBroken` | **발행** | 내구도가 0 이하로 떨어지는 순간. 살아 있음 → 부서짐 전이에서 **한 번만** |
| `GameEvents.OnTargetBroken` | **구독** | `CreatureManager` 가 수신하여 **실제로 치운 개체 수만큼** 리스폰 쿨다운을 예약한다 (#141) |
| `Target.HitReceived` (인스턴스) | **발행** | `Target.OnHit` 호출 시 `OnHitReceived` 메서드를 통해 피격 정보 통지 (#148) |
| `Target.HitReceived` (인스턴스) | **구독** | `CreatureMovement` (피격 상태 전이 및 도망), `CreatureHpDisplay` (HP 갱신 및 펀치 연출) |

`CreatureManager`, `CreatureMovement`, `CreatureHpDisplay` 는 `OnEnable` 구독 / `OnDisable` 해제 쌍을 준수한다.

### 읽는 밸런스 값

| CSV | 열 | 쓰는 곳 |
|---|---|---|
| `targets.csv` | `hp` | 초기 내구도, 원시 보상 계산 |
| `targets.csv` | `coin_mult`, `break_bonus` | `BreakInfo.RawCoin` |
| `targets.csv` | `stamina_restore` | `BreakInfo.StaminaRestore`. 회복형만 0 보다 크다 |
| `targets.csv` | `move_speed`, `turn_interval_sec` | `CreatureMovement` 배회 이동 속도 및 방향 전환 주기 |
| `stages.csv` | `spawn_count` | `CreatureManager` 동시 출현 목표 수 |
| `stages.csv` | `normal_ratio`, `anchor_ratio`, `runner_ratio`, `tourist_ratio` | `CreatureManager` 크리처 종류별 등장 확률 가중치 |
| `economy.csv` | `hit_radius_bonus` | 피격 반경 확대 비율 |
| `economy.csv` | `spawn_interval_sec` | `CreatureManager` 파괴 후 재등장 대기 시간 |

## 검증

Unity 6000.3.21f1, Edit Mode, 2026-09-17.

* [x] `TargetChecks.RunBatch()` **25건 통과**
* [x] `CreatureMovementChecks.RunBatch()` **11건 통과**
  * FSM 상태 정의 4종 일치
  * `CreatureMovement` 초기화 및 normal 속도(2.0), 방향전환주기(1.5초) 일치
  * 피격 시 BeingHit 전이, 경직 종료 후 Fleeing 전이, 도망 종료 후 Moving 전이 사이클 확인
  * 책상 평면 경계 초과 좌표 클램프 및 반사 방향 벡터 확인
  * 4종 크리처 move_speed 수치 파싱 일치 확인
  * 1단계 기준 spawn_count 6개 및 desk_expand 업그레이드 확장 반영 계산 확인
  * 기본 spawn_interval_sec 7.0초 및 업그레이드 단축 배율 계산 확인
* [x] 컴파일 에러·경고 0건

### #141 리스폰 회계 (2026-09-17)

`HandleTargetBroken` 은 정리 루프 결과와 무관하게 무조건 타이머를 하나 추가했다. 목록에 없는
대상의 파괴 이벤트가 들어오면 치운 것이 없는데도 예약이 쌓여 목표 동시 출현 수를 넘겼다.

에디터 배치로 실측한 값이다.

| 상황 | 수정 전 | 수정 후 |
|---|---|---|
| 정상 파괴 2건 | 목록 4 + 예약 2 = 6 (목표 6) | 같음 |
| 목록 밖 파괴 이벤트 2건 | 목록 6 + 예약 2 = 8 → 대기 후 **8마리** | 예약 0 → **6마리 유지** |

* [x] 검증 3건 추가 (목록 밖 이벤트가 예약을 만들지 않음 / 목표치 초과 스폰 없음 / 정상 파괴에서 필드+예약 합 = 목표치)
* [x] **수정 전 코드에서 검증이 실제로 실패하는 것을 확인했다** — 통과만 보면 아무것도 잡지 않는 검증일 수 있다
* [x] Edit Mode 검증 14건 통과

**타이머 리스트 구조는 유지했다.** 밸런스 시뮬레이터(`.github/scripts/simulate_balance.py`)가
"슬롯별 파괴 후 재등장 대기" 로 모델링하므로, 공용 타이머 하나로 바꾸면 N 마리 복구에 N 배
시간이 걸려 모델과 어긋난다.

**최초 이슈 본문의 전제("동시 파괴 시 개체 수 감소")는 재현되지 않아 정정했다.** `Target.OnHit`
이 죽는 순간 동기적으로 이벤트를 발행하고 같은 대상이 두 번 발행하지 않으므로, 첫 핸들러가
볼 때 죽어 있는 것은 언제나 한 마리뿐이다.

### #161 에디트 모드 검증 Destroy 오류 및 잔류 해결 (2026-09-18)

에디트 모드에서 검증 하네스(`CreatureMovementChecks`)가 돌 때 `Destroy(creature)` 가 에디트 모드에서
동작하지 않아 `Destroy may not be called from edit mode` 오류가 발생하고 오브젝트가 씬에 잔류하던 문제를
`SafeDestroy` (`Application.isPlaying ? Destroy : DestroyImmediate`) 로 해결했다.

* [x] `NCAI > 전체 검증 실행` 시 `Destroy may not be called from edit mode` 콘솔 에러 0건
* [x] `CreatureMovementChecks` 18건 전체 통과 (파괴된 크리처의 씬 즉시 파괴 단언 추가)
* [x] 검증 실행 후 씬에 임시 크리처 오브젝트 잔류 0건
* [x] 컴파일 에러·경고 0건

### #148 크리처 비율 폴백 제거 및 Target 이벤트 명명 정정 (2026-09-18)

* `CreatureManager.PickPrefabByStageRatio()` 에서 `stages.csv` 를 복제하던 4종 리터럴을 제거하고, `stageDef` 가 없거나 총합이 0 이하면 임의의 비율을 만들지 않고 스폰하지 않음 (`null` 반환)
* `Target.OnHitReceived` 이벤트를 `HitReceived` 로 변경하고 발행부 `OnHitReceived(HitInfo)` 메서드를 분리하여 `AGENTS.md` 명명 규칙 준수
* 구독자 2곳(`CreatureHpDisplay`, `CreatureMovement`)의 이벤트 구독 및 해제 코드 동기화
* [x] `CreatureMovementChecks` 에 `stageDef == null` 시 스폰 미수행 검증 추가 통과
* [x] `TargetChecks` 에 매 타격 시 `HitReceived` 이벤트 정상 발행 및 구독 해제 단언 검증 추가 통과
* [x] 전체 검증 하네스 15종 전수 통과
* [x] `convention-checker` 9대 규칙 전수 점검 통과 (위반 0건)

## 알려진 한계

* **파괴 연출이 없다.** 부서져도 오브젝트가 그대로 남거나 숨겨지는 연출은 작업 6.3 이다.
* **피격 반경 퍼크는 `CreatureManager` 가 넘겨 준다.** `Target` 이 `OnPerkChosen` 을 직접
  구독하지 않는 이유는 인스턴스가 여럿이라 구독자가 크리처 수만큼 늘기 때문이다
  ([퍼크 효과](perks.md)).
* ~~**피격 반경이 업그레이드를 반영하지 않는다.**~~ — #131 에서 `IUpgradeStats` 를 받아
  `Initialize()` 가 실효 비율로 반경을 잡는다. 스폰하는 쪽(`CreatureManager`)이 **`Initialize()`
  보다 먼저** `SetUpgradeStats` 를 불러야 반영된다. 반경이 스폰 시점에 정해지는 것은 그대로다 —
  런 도중 레벨이 오르지 않으므로 문제가 되지 않는다 ([업그레이드](upgrades.md) "다음 런부터").
* Play Mode 에서 타격 시 시각적 경직 모션 및 이펙트는 작업 6.3 에서 파티클 및 애니메이션과 함께 연출된다.

## 갱신 이력

| 날짜 | 이슈 | 누가 | 무엇이 바뀌었나 |
|---|---|---|---|
| 2026-09-16 | #16 | twins6375-art | 최초 작성 (그레이박스 4종, 내구도·피격, 파괴 발행) |
| 2026-09-17 | #17 | saltlake00 | 크리처 평면 2축 이동, FSM(Idle/Moving/BeingHit/Fleeing), CreatureManager 스폰·리스폰 구현 |
| 2026-09-17 | #141 | saltlake00 | 치운 개수만큼만 재등장을 예약하고 목표치 초과 스폰을 막는다. 검증 3건 추가 |
| 2026-09-18 | #161 | saltlake00 | SafeDestroy 도입으로 에디트 모드 검증 시 Destroy 오류 제거 및 씬 잔류 방지 (#161) |
| 2026-09-18 | #148 | saltlake00 | 크리처 스폰 비율 폴백 하드코딩 제거 및 Target.HitReceived 명명 규칙 정정, 검증 보강 |
| 2026.09.18 | #197 | saltlake00 | 씬 재로드 후 이펙트 풀 파괴 객체 접근 방어 및 Target 예외 격리 (2.12) |
