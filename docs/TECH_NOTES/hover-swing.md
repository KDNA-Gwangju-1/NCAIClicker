# 커서 조준 및 상시 자동 스윙

> 관련 이슈: #18 · 최종 수정: 2026-09-16

**이 문서는 로그다.** 이 기능을 고칠 때마다 갱신한다. 새 문서를 만들지 않는다.

## 무엇을 하는가

호버 망치는 대상 유무와 무관하게 `economy.csv`의 `hover_swing_interval_sec` 주기로 항상 스윙한다. 스윙 시점에 커서 스크린 좌표를 3D 레이캐스트해 책상 평면 월드 좌표와 실제 피격 대상(`IHittable`)을 동시에 판정한다. 게임 규칙은 [GDD](../GDD.md) 4절에 있으니 여기서 반복하지 않는다.

## 왜 이 방법인가

| 검토한 방법 | 채택 | 이유 |
|---|---|---|
| 3D `Collider` + `Physics.Raycast`로 `IHittable` 판정 | ✅ | GDD 4절이 요구하는 "콜라이더 25% 확대"가 3D 콜라이더 크기 조정만으로 그대로 적용되고, 기존 `IHittable`/`HitInfo` 계약([ARCHITECTURE](../ARCHITECTURE.md))에 바로 연결된다 |
| 스크린 좌표 거리 기반 판정 (`REFERENCE_ANALYSIS.md` 98행 대안) | ❌ | 별도의 스크린-월드 좌표 변환과 거리 계산 로직이 새로 필요하고, 3D 오브젝트 크기·카메라 각도가 바뀔 때마다 판정 반경을 따로 튜닝해야 해 `IHittable` 콜라이더 계약과 어긋난다 |
| 스윙 타이머: 잔여시간 이월(`while (_swingTimer >= interval) { _swingTimer -= interval; ... }`) | ✅ | 프레임이 밀려도(예: 순간 랙) 스윙 박자가 누적 오차 없이 유지된다. 한 프레임에 두 번 이상 스윙이 밀려도 모두 처리한다 |
| 스윙 타이머: 초과 시 0으로 리셋 | ❌ | 프레임 드랍이 반복되면 실제 초당 스윙 횟수가 설계값보다 줄어들며, 그 오차가 보정되지 않고 누적된다 |
| Input System `Mouse.current.position` | ✅ | 프로젝트 Player Settings의 Active Input Handling이 Input System으로 설정되어 있다. Play Mode에서 실제로 확인함 |
| 레거시 `UnityEngine.Input.mousePosition` | ❌ | 위 설정에서 `InvalidOperationException`을 던진다. Play Mode 실행 중 실제로 발생해 수정한 이력이 있다 |
| 마우스 디바이스가 없을 때도 "미스"로 스윙을 계속 발행 | ✅ | 완료 기준이 "대상 유무와 무관한 상시 스윙"이다. 입력 장치 부재를 이유로 스윙 자체를 멈추면 이 기준을 어긴다 |
| 마우스 디바이스가 없으면 해당 프레임 스윙 스킵 | ❌ | 스윙 박자가 장치 상태에 좌우되게 되어 "상시" 요구사항과 충돌한다 |

## 구조

```mermaid
flowchart LR
  subgraph Core["코어 플레이"]
    swing[HammerSwingController<br/>상시 스윙 타이머·커서 레이캐스트]
  end

  subgraph Targets["피격 대상"]
    hittable[IHittable 구현체<br/>저금통 등]
  end

  events{{"GameEvents<br/>(정적 이벤트)"}}

  swing -- "Physics.Raycast 명중 시<br/>OnHit(HitInfo) 직접 호출" --> hittable
  swing -- "OnSwingResolved(Hover, isHit) 발행<br/>(명중 여부와 무관하게 매 스윙)" --> events
```

<!-- OnHit 은 IHittable 계약상의 직접 호출이라 이벤트 노드를 거치지 않는다. OnSwingResolved 는 GameEvents 를 거치므로 이벤트 노드를 경유해 그렸다 -->

| 클래스 | 경로 | 하는 일 |
|---|---|---|
| `HammerSwingController` | `Assets/Scripts/Runtime/Core/HammerSwingController.cs` | 상시 스윙 타이머 관리, 커서 스크린 좌표 → 책상 평면 월드 좌표 변환, `Physics.Raycast`로 `IHittable` 판정, `OnHit` 호출 및 `OnSwingResolved` 발행 |

### 이벤트

| 이벤트 | 발행/구독 | 언제 |
|---|---|---|
| `GameEvents.OnSwingResolved(HitSource, bool)` | 발행 | 매 스윙마다 (명중 여부와 무관하게 `HitSource.Hover`로 발행) |

### 읽는 밸런스 값

| CSV | 열 | 쓰는 곳 |
|---|---|---|
| `Assets/GameData/Balance/economy.csv` | `hover_swing_interval_sec` | 스윙 주기 계산 (기본 0.5초 = 초당 2회) |
| `Assets/GameData/Balance/economy.csv` | `base_hit_power` | 명중 시 `HitInfo.Power`로 전달해 `IHittable.OnHit`에 사용 |

## 검증

Unity 6000.3.21f1 에디터 Play Mode, 2026-09-16.

- [x] Play Mode 실행 후 콘솔 에러 없음 확인 — 최초 구현 시 레거시 `UnityEngine.Input` 사용으로 `InvalidOperationException` 발생, `Mouse.current` 로 수정 후 재실행해 에러 없음 확인
- [x] `hover_swing_interval_sec` 을 0.15 → 0.5로 수정 후 `NCAI > 밸런스 CSV 임포트` 실행, `BalanceData.asset` 의 `HoverSwingIntervalSec` 값이 0.5로 반영됨을 확인
- [x] 값 변경 후 Play Mode 재실행, 콘솔 에러 없음 재확인
- [ ] 실제 `IHittable` 구현 대상(저금통)에 커서를 올려 명중 판정이 성공하고 `OnHit` 이 호출되는 경로 — 미검증 (씬에 테스트용 대상 오브젝트가 아직 없음)
- [ ] 마우스 디바이스가 없는 환경에서 "미스" 스윙이 계속 발행되는지 — 미검증 (에디터 환경에서 마우스 미연결 상태를 재현하지 않음)

## 알려진 한계

- 스킬 해금에 따라 스윙 속도가 빨라지는 기능은 이번 범위에서 제외했다. 밸런스 값을 코드가 아닌 별도 승수로 다루려면 `IEconomyService`/`BalanceData` 계약 변경이 필요해 `AGENTS.md` 기준 "공용 계약 변경" 이슈로 별도 처리해야 한다.
- 씬에 실제 `IHittable` 구현 대상이 아직 없어 명중 판정 경로는 코드 리뷰 수준으로만 확인했다. 저금통 프리팹이 추가되면 Play Mode 로 재검증이 필요하다.
- `CursorWorldPosition` 프로퍼티는 매 스윙마다 계산해 두었지만 아직 시각적 커서 마커 등 소비자가 없다. 향후 조준 UI 작업에서 사용할 예정이다.
- `_hittableLayerMask` 기본값이 `~0`(전체 레이어)이라 프로젝트에 레이어가 세분화되면 과잉 판정될 수 있다. 대상 레이어가 정해지면 인스펙터에서 좁혀야 한다.

## 갱신 이력

| 날짜 | 이슈 | 누가 | 무엇이 바뀌었나 |
|---|---|---|---|
| 2026-09-16 | #18 | Claude | 최초 작성 |
