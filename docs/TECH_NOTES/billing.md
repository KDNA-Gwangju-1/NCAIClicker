# 하루 진행과 청구서

> 관련 이슈: #27, #28 · 최종 수정: 2026-09-17

**이 문서는 로그다.** 이 기능을 고칠 때마다 갱신한다. 새 문서를 만들지 않는다.

## 무엇을 하는가

런 종료를 하루 종료로 집계하고, 하루가 시작될 때마다 `stages.csv` 단계값으로 청구서(금액·마감일)를 발행한다. HUD는 남은 일수와 금액을 항상 보여준다. 마감 전이면 `TryPay`로 언제든 조기 납부할 수 있고, 납부에 성공하면 `perks.csv`에서 3종을 무작위로 뽑아 `OnPerkOffered`로 알린 뒤 `TryChoosePerk`로 하나를 고른다(#28). 퍼크 효과의 실제 게임플레이 반영은 이 클래스 범위 밖 — 아래 "알려진 한계" 참고. 대출·파산 판정(4.3~4.4, 이 문서 범위 밖)은 `IBillService`의 스텁(`TryTakeLoan`/`TryRepayLoan`)으로만 계약을 지키고 실제 동작은 아직 없다. 게임 규칙은 [GDD](../GDD.md)에 있으니 여기서 반복하지 않는다.

## 왜 이 방법인가

| 검토한 방법 | 채택 | 이유 |
|---|---|---|
| `BillManager`가 `EconomyManager`처럼 싱글톤 + `IBillService` 구현, `Managers` 프리팹에 부착 | ✅ | 기존 매니저(EconomyManager/StaminaManager/SaveManager) 패턴과 동일해 팀이 이미 아는 구조를 그대로 쓴다. `ManagerBootstrap`이 조립 지점에서 `EconomyManager.SetBillService`로 연결한다 |
| 별도 `StageManager`를 새로 만들어 단계 진행을 관리 | ❌ | 이슈 #27 범위 밖(공용 계약에 없는 새 매니저). 당장은 `BillManager` 내부의 `_billIndex` 순번으로 `stages.csv`를 순서대로 읽는 것으로 충분하고, 단계 진행 전담 매니저가 필요해지면 그때 분리한다 |
| HUD가 매 프레임 `IBillService.DaysLeft`를 폴링 | ❌ | ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다"에 어긋난다. `GameEvents.OnBillDueSoon`(이미 선언돼 있었지만 아무도 발행하지 않던 이벤트)을 `BillManager.BeginRun()` 끝에서 발행하도록 고쳐 HUD가 구독하게 했다 |
| `OnBillDueSoon`을 "마감 임박" 때만(예: 2일 이하) 발행 | ❌ | 이슈 #27 완료 기준이 "HUD는 항상 일수·금액을 보여준다"라 매일 갱신이 필요하다. `contracts.md`의 "마감 임박 시" 설명과는 결이 다르지만 인자·발행 메서드 시그니처(`Action<int>`, `PublishBillDueSoon(int)`)는 그대로이므로 계약 변경이 아니다 — 다음에 "진짜 임박" 필터가 필요해지면 그때 구독측(HUD)에서 걸러도 된다 |
| HUD를 `Game.unity`에 직접 배치 | ❌ | `Game.unity`은 "코어 플레이" 모듈 소유 씬이라 남의 씬을 고치면 안 된다(AGENTS.md). 대신 `Assets/Prefabs/UI/BillHud.prefab`(Canvas + TextMeshProUGUI)을 독립 프리팹으로 만들어 넘긴다 — 코어 플레이 담당이 씬에 배치하면 된다 |
| (#28) 퍼크 4종을 `IPerkEffect` 등으로 서브클래스화 | ❌ | 후보가 정확히 4종 고정이고 각자 적용 대상 시스템이 다를 뿐 분기 로직은 없다. `PerkType` enum + `PerkDef`(값·지속시간) 데이터 하나로 충분하다 — 과설계 금지(ponytail) |
| (#28) `TryPay`를 `BillManager`에 직접 추가 vs 새 `PaymentManager` 신설 | ✅ 전자 | `BillManager`가 이미 `_activeBill`과 마감 판정을 갖고 있다. 새 매니저를 만들면 활성 청구서 상태를 두 곳에서 동기화해야 한다 |
| (#28) 코인 차감을 `BillManager`가 직접 지갑 필드를 건드림 | ❌ | "코인 배율·차감은 EconomyManager 안에서만"(AGENTS.md). `BillManager`는 `IEconomyService.TrySpendCoin`만 호출하고, `EconomyManager`가 `ManagerBootstrap`에서 `SetEconomyService`로 주입된다(`EconomyManager`가 `SetBillService`로 자신을 넘기는 것과 대칭 구조) |
| (#28) 퍼크 선택 시 즉시 스태미나/코인배율/타격력 등에 반영 | ❌ | 각 수치는 StaminaManager·EconomyManager·코어 플레이 등 다른 모듈 소유다. `BillManager`는 `OnPerkChosen(string perkId)` 이벤트만 발행하고, 각 시스템이 `BalanceData.GetPerk(id)`로 값을 읽어 스스로 적용하는 훅만 열어 둔다(이슈 범위 밖, 알려진 한계 참고) |
| (#28) 퍼크 고르는 메서드 이름을 `ChoosePerk` 로 둠 | ❌ | `IBillService`의 다른 액션 메서드(`TryPay`/`TryTakeLoan`/`TryRepayLoan`)가 전부 실패 가능성을 이름에 담는 `Try*` 접두어라, `TryChoosePerk`로 맞췄다(convention-checker 지적) |

## 구조

```mermaid
flowchart LR
  subgraph Economy["경제"]
    bill[BillManager<br/>하루 진행·청구서 발행·조기 납부·퍼크 제시]
    econ[EconomyManager<br/>배율·대출 징수·코인 차감]
  end

  subgraph UI["UI·연출"]
    hud[BillHud<br/>D-일수 · 금액 표시]
  end

  events{{"GameEvents"}}
  csvStages[(stages.csv<br/>bill_amount · due_days)]
  csvPerks[(perks.csv<br/>id · value · duration_sec)]

  csvStages -- "BalanceData 로 임포트" --> bill
  csvPerks -- "BalanceData 로 임포트" --> bill
  bill -- "OnBillIssued(Bill) 발행" --> events
  bill -- "OnBillDueSoon(int) 발행" --> events
  bill -- "OnDayEnded(int) 발행" --> events
  bill -- "OnBillPaid(Bill) 발행" --> events
  bill -- "OnPerkOffered(string[]) 발행" --> events
  bill -- "OnPerkChosen(string) 발행" --> events
  events -- "구독" --> hud
  bill -- "IBillService 통로<br/>(SetBillService 로 조립)" --> econ
  bill -- "IEconomyService.TrySpendCoin<br/>(SetEconomyService 로 조립)" --> econ
```

<!-- GameEvents 를 거치는 관계는 이벤트 노드를 경유해 그린다. 모듈끼리 직접 잇지 않는다 -->

| 클래스 | 경로 | 하는 일 |
|---|---|---|
| `BillManager` | `Assets/Scripts/Runtime/Economy/BillManager.cs` | `IBillService` 구현. `BeginRun`으로 하루 시작(날짜 증가·청구서 발행), `EndRun`으로 하루 종료(`OnDayEnded` 발행), `TryPay`로 조기 납부(`IEconomyService.TrySpendCoin` 경유) 성공 시 퍼크 3종을 뽑아 `OnPerkOffered` 발행, `TryChoosePerk`로 하나를 고르면 `OnPerkChosen` 발행. 대출은 스텁 |
| `BillHud` | `Assets/Scripts/Runtime/UI/BillHud.cs` | `OnBillIssued`/`OnBillDueSoon` 구독, `TextMeshProUGUI`에 "D-N  N원" 형식으로 표시 |
| (프리팹) | `Assets/Prefabs/UI/BillHud.prefab` | Canvas(ScreenSpaceOverlay) + `BillLabel`(우상단, `BillHud` 부착) |
| `BillManagerChecks` | `Assets/Scripts/Editor/BillManagerChecks.cs` | EditMode 배치 검증. `MenuItem` 없이 `RunBatch()`를 외부에서 호출한다 |

### 이벤트

| 이벤트 | 발행/구독 | 언제 |
|---|---|---|
| `GameEvents.OnBillIssued` | `BillManager`가 발행, `BillHud`가 구독 | 새 청구서가 만들어질 때(활성 청구서가 없는 상태에서 `BeginRun` 호출 시) |
| `GameEvents.OnBillDueSoon` | `BillManager`가 발행, `BillHud`가 구독 | 활성 청구서가 있는 상태로 `BeginRun`이 끝날 때마다(하루마다). 인자는 그 시점의 `DaysLeft` |
| `GameEvents.OnDayEnded` | `BillManager`가 발행 | `EndRun` 호출 시. 런당 한 번 = 날짜당 한 번 |
| `GameEvents.OnBillPaid` | `BillManager`가 발행 | `TryPay` 성공 시(#28). 구독자 아직 없음 — UI는 이슈 범위 밖 |
| `GameEvents.OnPerkOffered` | `BillManager`가 발행 | `TryPay` 성공 직후, `perks.csv`에서 무작위로 뽑은 퍼크 id 3개(#28). 구독자 아직 없음 — 선택 UI는 이슈 범위 밖 |
| `GameEvents.OnPerkChosen` | `BillManager`가 발행 | `TryChoosePerk` 성공 시, 고른 퍼크 id(#28). 구독자 아직 없음 — 효과 적용은 각 시스템 몫(알려진 한계 참고) |

### 읽는 밸런스 값

| CSV | 열 | 쓰는 곳 |
|---|---|---|
| `Assets/GameData/Balance/stages.csv` | `bill_amount` | 청구서 금액(`Bill.Amount`) |
| `Assets/GameData/Balance/stages.csv` | `due_days` | 청구서 마감일 계산(`Bill.DueDay = IssuedDay + due_days - 1`) |
| `Assets/GameData/Balance/perks.csv` | `id` | `PerkType`(스냅케이스) 매칭 — `PerkDef.Id`, `BalanceData.GetPerk(id)` 조회 키 |
| `Assets/GameData/Balance/perks.csv` | `value` | 퍼크 효과 수치(`PerkDef.Value`). 실제 적용은 각 시스템 몫 — 알려진 한계 참고 |
| `Assets/GameData/Balance/perks.csv` | `duration_sec` | 지속시간(`PerkDef.DurationSec`). `coin_gain_boost`만 0보다 커야 하고 나머지는 0이어야 한다(임포터 검증) |

## 검증

Unity 6000.3.21f1 헤드리스 배치 실행, 2026-09-17.

- [x] 컴파일: `unity run . -- -executeMethod NCAIClicker.EditorTools.BillManagerChecks.RunBatch -logFile -` → 도메인 리로드 포함 정상 종료, `error CS` 0건
- [x] `BillManagerChecks.RunBatch()`: `[BillManagerChecks] PASS 15 checks.` — 기존 6건(#27, 위 최초 작성 시점 내용) + 신규 9건(#28): `TryPay` 성공 시 코인 차감(`IEconomyService.TrySpendCoin` 경유)·`OnBillPaid` 발행·`_activeBill` 해제, 잔액 부족 시 `TryPay` 실패, 이미 낸 청구서 재납부 실패, `TryPay` 성공 시 `perks.csv` 4종 중 3개를 중복 없이 뽑아 `OnPerkOffered` 발행, 후보에 없는 id로 `TryChoosePerk` 실패(후보 목록·`OnPerkChosen` 불변), 후보 중 하나로 `TryChoosePerk` 성공(`OnPerkChosen` 발행·후보 목록 비움), 이미 고른 뒤 재선택 실패
- [x] `EconomyManagerChecks.RunBatch()` 회귀: `[EconomyManagerChecks] PASS 8 checks.` — `IBillService` 확장(`ActiveBill`/`OfferedPerkIds`/`TryChoosePerk` 추가)으로 `FakeBillService`를 고친 뒤에도 기존 배선·대출 징수 검증이 깨지지 않음을 확인
- [x] `BillHud.prefab` 생성(#27): 임시 `-executeMethod` 스크립트(`TempBillHudPrefabBuilder.Build`, 실행 후 삭제)로 `PrefabUtility.SaveAsPrefabAsset` → `[TempBillHudPrefabBuilder] 저장 완료` 로그 확인, 정상 종료
- [x] `perks.csv` → `NCAI > 밸런스 CSV 임포트`로 `BalanceData.asset` 재생성, 임포터 검증(4종 고정·`id` 중복 없음·`CoinGainBoost`만 `duration_sec > 0`) 통과
- [ ] 에디터 Play Mode에서 실제 HUD·퍼크 선택 UI 확인 — `Game.unity`에 프리팹을 배치하는 것은 코어 플레이 모듈 담당 몫이고, 퍼크 선택 UI 자체가 아직 없다(알려진 한계 참고). 미검증
- [ ] `GameManager`가 `BeginRun`/`EndRun`을 실제로 호출하는 배선(작업 2.5) — 아직 없다. 미검증

## 알려진 한계

- `GameManager`가 아직 `BillManager.BeginRun()`/`EndRun()`을 호출하지 않는다(작업 2.5, 이 이슈 범위 밖). 지금은 `BillManagerChecks`가 직접 호출해서만 검증했다.
- 단계 진행(`_billIndex`)을 `BillManager`가 자체 순번으로 관리한다. 전담 단계 매니저가 생기면 이 필드를 걷어내고 그쪽 조회로 바꿔야 한다.
- 대출(#29)·파산 판정(#30)은 스텁이라 청구서를 기한 내에 안 내면 절대 사라지지 않는다. HUD의 "D-0"은 기한 초과 상태를 그대로 보여줄 뿐 파산 처리로 이어지지 않는다.
- `BillManager`의 날짜·청구서·퍼크 후보(`OfferedPerkIds`) 상태는 저장/복원되지 않는다(`SaveManager` 미연동). ARCHITECTURE.md는 `SaveData.OfferedPerkIds`/`PendingPerkIds` 필드를 이미 계약해 뒀으므로, 저장 연동은 그 필드에 채워 넣는 방식으로 붙이면 된다.
- `BillHud.prefab`은 `Game.unity`에 아직 배치되지 않았다. 코어 플레이 담당이 씬에 넣어야 실제로 보인다.
- 퍼크 선택(`OnPerkChosen`)의 실제 게임플레이 효과 적용이 없다(#28 범위 밖). `StaminaRestore`/`CoinGainBoost`/`HitPowerBoost`/`HitRadiusBoost` 각각을 스태미나·경제·코어 플레이 시스템이 `BalanceData.GetPerk(id)`로 읽어 스스로 적용해야 한다 — 지금은 `OnPerkChosen`을 구독하는 곳이 없다.
- 퍼크 선택 UI가 없다. GDD 6.9절이 요구하는 "선택 중 게임 시계 정지" 같은 연출도 그 UI가 생긴 뒤에야 붙일 수 있다.

## 갱신 이력

| 날짜 | 이슈 | 누가 | 무엇이 바뀌었나 |
|---|---|---|---|
| 2026-09-17 | #27 | Claude | 최초 작성 — `BillManager`/`BillHud` 구현, `OnBillDueSoon` 발행 연결 |
| 2026-09-17 | #28 | Claude | `TryPay` 조기 납부 구현, `perks.csv`/`PerkType`/`PerkDef` 추가, 납부 성공 시 퍼크 3종 제시(`OnPerkOffered`)·선택(`TryChoosePerk`, `OnPerkChosen`) |
