# 하루 진행과 청구서

> 관련 이슈: #27, #28, #29, #150, #30, #164, #92 · 최종 수정: 2026-09-18

**이 문서는 로그다.** 이 기능을 고칠 때마다 갱신한다. 새 문서를 만들지 않는다.

## 무엇을 하는가

런 종료를 하루 종료로 집계하고, 하루가 시작될 때마다 `stages.csv` 단계값으로 청구서(금액·마감일)를 발행한다. HUD는 남은 일수와 금액을 항상 보여준다. 마감 전이면 `TryPay`로 언제든 조기 납부할 수 있고, 납부에 성공하면 `perks.csv`에서 3종을 무작위로 뽑아 `OnPerkOffered`로 알린 뒤 `TryChoosePerk`로 하나를 고른다(#28). 퍼크 효과의 실제 게임플레이 반영은 이 클래스 범위 밖 — 아래 "알려진 한계" 참고. 대출(#29)은 두 번째 청구서부터 활성 청구서 금액을 한도로 빌리고(`TryTakeLoan`), 이자를 더한 전액을 한 번에 갚는다(`TryRepayLoan`). 미상환 기간에는 `LoanDailyCut`이 0이 아니게 되어 `EconomyManager`가 수입에서 그만큼 떼며, 그 징수분은 부채를 줄이지 않는다. **파산 판정(#30)도 여기서 한다** — 아래 "마감 미납과 파산" 절. 게임 규칙은 [GDD](../GDD.md)에 있으니 여기서 반복하지 않는다.

## 왜 이 방법인가

| 검토한 방법 | 채택 | 이유 |
|---|---|---|
| `BillManager`가 `EconomyManager`처럼 싱글톤 + `IBillService` 구현, `Managers` 프리팹에 부착 | ✅ | 기존 매니저(EconomyManager/StaminaManager/SaveManager) 패턴과 동일해 팀이 이미 아는 구조를 그대로 쓴다. `ManagerBootstrap`이 조립 지점에서 `EconomyManager.SetBillService`로 연결한다 |
| 별도 `StageManager`를 새로 만들어 단계 진행을 관리 | ❌ → **뒤집힘** | 이슈 #27 범위 밖이라 `_billIndex` 순번으로 `stages.csv`를 순서대로 읽었다. **#150 에서 뒤집혔다** — `StageGoalManager`가 `IStageService`로 단일 출처가 되었고 `_billIndex`는 누적 청구서 순번으로만 남는다 |
| HUD가 매 프레임 `IBillService.DaysLeft`를 폴링 | ❌ | ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다"에 어긋난다. `GameEvents.OnBillDueSoon`(이미 선언돼 있었지만 아무도 발행하지 않던 이벤트)을 `BillManager.BeginRun()` 끝에서 발행하도록 고쳐 HUD가 구독하게 했다 |
| `OnBillDueSoon`을 "마감 임박" 때만(예: 2일 이하) 발행 | ❌ | 이슈 #27 완료 기준이 "HUD는 항상 일수·금액을 보여준다"라 매일 갱신이 필요하다. `contracts.md`의 "마감 임박 시" 설명과는 결이 다르지만 인자·발행 메서드 시그니처(`Action<int>`, `PublishBillDueSoon(int)`)는 그대로이므로 계약 변경이 아니다 — 다음에 "진짜 임박" 필터가 필요해지면 그때 구독측(HUD)에서 걸러도 된다 |
| HUD를 `Game.unity`에 직접 배치 | ❌ | `Game.unity`은 "코어 플레이" 모듈 소유 씬이라 남의 씬을 고치면 안 된다(AGENTS.md). 대신 `Assets/Prefabs/UI/BillHud.prefab`(Canvas + TextMeshProUGUI)을 독립 프리팹으로 만들어 넘긴다 — 코어 플레이 담당이 씬에 배치하면 된다 |
| (#28) 퍼크 4종을 `IPerkEffect` 등으로 서브클래스화 | ❌ | 후보가 정확히 4종 고정이고 각자 적용 대상 시스템이 다를 뿐 분기 로직은 없다. `PerkType` enum + `PerkDef`(값·지속시간) 데이터 하나로 충분하다 — 과설계 금지(ponytail) |
| (#28) `TryPay`를 `BillManager`에 직접 추가 vs 새 `PaymentManager` 신설 | ✅ 전자 | `BillManager`가 이미 `_activeBill`과 마감 판정을 갖고 있다. 새 매니저를 만들면 활성 청구서 상태를 두 곳에서 동기화해야 한다 |
| (#28) 코인 차감을 `BillManager`가 직접 지갑 필드를 건드림 | ❌ | "코인 배율·차감은 EconomyManager 안에서만"(AGENTS.md). `BillManager`는 `IEconomyService.TrySpendCoin`만 호출하고, `EconomyManager`가 `ManagerBootstrap`에서 `SetEconomyService`로 주입된다(`EconomyManager`가 `SetBillService`로 자신을 넘기는 것과 대칭 구조) |
| (#28) 퍼크 선택 시 즉시 스태미나/코인배율/타격력 등에 반영 | ❌ | 각 수치는 StaminaManager·EconomyManager·코어 플레이 등 다른 모듈 소유다. `BillManager`는 `OnPerkChosen(string perkId)` 이벤트만 발행하고, 각 시스템이 `BalanceData.GetPerk(id)`로 값을 읽어 스스로 적용하는 훅만 열어 둔다(이슈 범위 밖, 알려진 한계 참고) |
| (#28) 퍼크 고르는 메서드 이름을 `ChoosePerk` 로 둠 | ❌ | `IBillService`의 다른 액션 메서드(`TryPay`/`TryTakeLoan`/`TryRepayLoan`)가 전부 실패 가능성을 이름에 담는 `Try*` 접두어라, `TryChoosePerk`로 맞췄다(convention-checker 지적) |
| (#29) 일일 징수율을 `loan_daily_cut_min`~`max` 범위에서 무작위로 뽑음 | ❌ | 같은 선택이 매번 다른 결과를 내면 7.2 밸런싱 실측도 Edit Mode 검증도 성립하지 않는다. 사채업자 분위기는 살지만 검증 비용이 그보다 크다 |
| (#29) 빌린 금액에 비례해 정함 — 전액이면 상한, 소액이면 하한 | ✅ | 결정적이라 검증·실측이 가능하고 "많이 빌릴수록 비싸다"는 저울질이 생긴다. `Loan.DailyCut`을 대출 시 1회 결정하고 고정한다는 ARCHITECTURE 계약과도 맞는다 |
| (#29) 대출 한도를 두지 않고 호출측(UI)에 맡김 | ❌ | 한도가 없으면 한 번에 빚을 몰아 나선형 파산이 난다. BALANCE.md 4절이 "청구서 전액을 빌리면"을 전제로 상환 가능성을 검증하므로 활성 청구서 금액을 상한으로 삼았다 |
| (#30) 파산 판정을 `BillManager`에서 | ✅ | ARCHITECTURE 1절의 매니저 표가 이 매니저에 "하루 진행, 청구서 마감, 대출과 징수, **파산 판정**"을 맡겨 두었다. 마감일·미납 여부를 아는 곳이 여기 하나뿐이기도 하다 |
| (#30) 파산 시 상태를 지운 **뒤** `OnBankrupt` 발행 | ❌ | 받는 쪽이 이미 초기화된 상태를 보게 되어 결과 화면(6.2)이 무엇이 실패했는지 읽을 수 없다. 알리는 것이 먼저다 |
| (#30) 지갑 비우기를 `IEconomyService.TrySpendCoin(CurrentCoin)` 으로 | ❌ | 정수만 떼므로 **소수 잔여가 남는다.** 계약 7번이 "소수 잔여는 파산 시 버린다"고 정했다 |
| (#30) `IWalletPersistence` 를 조립 통로로 주입받아 `RestoreWallet(0, "0")` | ❌ | 한 번 넣었다가 되돌렸다. 그 인터페이스는 **"SaveManager 만 쓴다"**로 사용자가 묶여 있어 `BillManager` 가 두 번째 사용자가 된다 — 공용 계약 확장이라 이슈가 먼저다 |
| (#30) `EconomyManager` 가 `OnBankrupt` 를 구독해 스스로 비움 | ❌ | ARCHITECTURE 3절이 "`OnBankrupt`는 GameManager만 구독"으로 막아 두었다. 같은 이유로 막힌다 |
| (#30) 단계 초기화를 `IStageService.RestoreStage(0)` 로 | ✅ | #150 이 만든 계약을 **쓰기만 한다** — 확장이 필요 없다. 그쪽이 용도까지 "파산 처리용"으로 적어 두었다 |
| (#30) 지갑 초기화를 이 카드에서 빼고 계약 이슈로 | ✅ | 셋 다 공용 문서를 고쳐야 하는데, 규칙은 **구현 전에** 이슈를 먼저 올리라고 한다. 판정·발행·`BillManager` 자체 초기화는 계약을 건드리지 않으므로 먼저 넣는다 |
| (#29) 저장·복원을 이 이슈에서 함께 붙임 | ❌ | `IBillService`에 복원 통로를 더하는 공용 계약 변경이라 규칙상 별도 이슈가 먼저다(AGENTS.md). 날짜·청구서·퍼크 후보 저장까지 함께 걸리는 범위라 #29 완료 기준을 넘는다 |
| (#158) `IWalletPersistence` 소비자에 `BillManager` 추가 (A안) | ✅ | #30 에서 이슈로 발의해 합의를 거쳤다. 지금 필요한 곳이 `SaveManager` 외에 하나(`BillManager`)뿐이라 소비자 목록만 넓히는 것이 새 계약(`IRoundResettable` 등)을 파는 것보다 작다 — 소비처가 둘 이상 될 때 승격한다는 #116 의 교훈을 따랐다 |
| (#158) 전용 `IRoundResettable` 계약 신설 (B안) | ❌ | 구현자가 `EconomyManager` 하나뿐이라 계약만 만들고 소비처가 없던 #116 의 전철을 밟을 위험이 컸다. 3.7(#150)이 되돌릴 대상을 더 만들면 그때 승격한다 |
| (#158) `EconomyManager` 가 `OnBankrupt` 를 직접 구독 (C안) | ❌ | `OnBankrupt` 는 GameManager 만 구독한다는 제한(ARCHITECTURE 3절)이 흐려지고, "종료 순서는 GameManager 가 조정한다"는 원칙과도 어긋난다 |

## 구조

```mermaid
flowchart LR
  subgraph Economy["경제"]
    bill[BillManager<br/>하루 진행·청구서 발행·조기 납부·퍼크 제시·파산 판정]
    econ[EconomyManager<br/>배율·대출 징수·코인 차감]
    stage[StageGoalManager<br/>단계 단일 출처]
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
  bill -- "OnBankrupt 발행 (#30)" --> events
  bill -- "OnBillPaid(Bill) 발행" --> events
  bill -- "OnPerkOffered(string[]) 발행" --> events
  bill -- "OnPerkChosen(string) 발행" --> events
  events -- "구독" --> hud
  bill -- "IBillService 통로<br/>(SetBillService 로 조립)" --> econ
  bill -- "IEconomyService.TrySpendCoin<br/>(SetEconomyService 로 조립)" --> econ
  bill -- "IStageService 조회·RestoreStage<br/>(SetStageService 로 조립, #150·#30)" --> stage
```

<!-- GameEvents 를 거치는 관계는 이벤트 노드를 경유해 그린다. 모듈끼리 직접 잇지 않는다 -->

| 클래스 | 경로 | 하는 일 |
|---|---|---|
| `BillManager` | `Assets/Scripts/Runtime/Economy/BillManager.cs` | `IBillService` 구현. `BeginRun`으로 하루 시작(날짜 증가·청구서 발행), `EndRun`으로 하루 종료(`OnDayEnded` 발행), `TryPay`로 조기 납부(`IEconomyService.TrySpendCoin` 경유) 성공 시 퍼크 3종을 뽑아 `OnPerkOffered` 발행, `TryChoosePerk`로 하나를 고르면 `OnPerkChosen` 발행. 대출은 `TryTakeLoan`으로 빌리고 `TryRepayLoan`으로 갚으며, 징수율은 `LoanDailyCut`으로만 내보낸다(#29). 마감 미납 파산을 판정해 `OnBankrupt`를 발행하고 회차를 되돌린다(#30) |
| `BankruptcyChecks` | `Assets/Scripts/Editor/BankruptcyChecks.cs` | 파산 판정 경계와 회차 초기화 범위의 Edit Mode 검증 8건 (#30) |
| `RunWiringChecks` | `Assets/Scripts/Editor/RunWiringChecks.cs` | 런 경계 **배선과 순서** 검증 5건 (#164) |
| `BillHud` | `Assets/Scripts/Runtime/UI/BillHud.cs` | `OnBillIssued`/`OnBillDueSoon` 구독, `TextMeshProUGUI`에 "D-N  N원" 형식으로 표시 |
| (프리팹) | `Assets/Prefabs/UI/BillHud.prefab` | Canvas(ScreenSpaceOverlay) + `BillLabel`(우상단, `BillHud` 부착) |
| `BillManagerChecks` | `Assets/Scripts/Editor/BillManagerChecks.cs` | EditMode 배치 검증. `MenuItem` 없이 `RunBatch()`를 외부에서 호출한다 |

### 이벤트

| 이벤트 | 발행/구독 | 언제 |
|---|---|---|
| `GameEvents.OnBillIssued` | `BillManager`가 발행, `BillHud`·`AudioManager`(6.4)가 구독 | 새 청구서가 만들어질 때(활성 청구서가 없는 상태에서 `BeginRun` 호출 시) |
| `GameEvents.OnBillDueSoon` | `BillManager`가 발행, `BillHud`가 구독 | 활성 청구서가 있는 상태로 `BeginRun`이 끝날 때마다(하루마다). 인자는 그 시점의 `DaysLeft` |
| `GameEvents.OnDayEnded` | `BillManager`가 발행 | `EndRun` 호출 시. 런당 한 번 = 날짜당 한 번 |
| `GameEvents.OnBankrupt` | `BillManager`가 발행 | `EndRun` 에서 마감 미납이 확정된 순간(#30). **회차를 되돌리기 전에** 발행한다 |
| `GameEvents.OnBillPaid` | `BillManager`가 발행, `AudioManager`(6.4)가 구독 | `TryPay` 성공 시(#28) |
| `GameEvents.OnPerkOffered` | `BillManager`가 발행, `PerkChoiceController`가 구독(#92) | `TryPay` 성공 직후, `perks.csv`에서 무작위로 뽑은 퍼크 id 3개(#28). 받은 쪽이 선택 화면을 띄운다 — [퍼크 3장 선택 화면](perk-choice-ui.md) |
| `GameEvents.OnPerkChosen` | `BillManager`가 발행, **네 소유자가 구독**(#126) | `TryChoosePerk` 성공 시, 고른 퍼크 id(#28). 효과 적용은 각 시스템 몫 — [퍼크 효과](perks.md) |

### 읽는 밸런스 값

| CSV | 열 | 쓰는 곳 |
|---|---|---|
| `Assets/GameData/Balance/stages.csv` | `bill_amount` | 청구서 금액(`Bill.Amount`) |
| `Assets/GameData/Balance/stages.csv` | `due_days` | 청구서 마감일 계산(`Bill.DueDay = IssuedDay + due_days - 1`) |
| `Assets/GameData/Balance/perks.csv` | `id` | `PerkType`(스냅케이스) 매칭 — `PerkDef.Id`, `BalanceData.GetPerk(id)` 조회 키 |
| `Assets/GameData/Balance/perks.csv` | `value` | 퍼크 효과 수치(`PerkDef.Value`). 실제 적용은 각 시스템 몫 — 알려진 한계 참고 |
| `Assets/GameData/Balance/perks.csv` | `duration_sec` | 지속시간(`PerkDef.DurationSec`). `coin_gain_boost`만 0보다 커야 하고 나머지는 0이어야 한다(임포터 검증) |
| `Assets/GameData/Balance/bills.csv` | `loan_unlock_bill_index` | 대출 해금 순번 — 손에 든 청구서가 몇 번째부터 빌릴 수 있나 |
| `Assets/GameData/Balance/bills.csv` | `loan_interest_rate` | 상환액 계산(`CalculateOwed`, 소수 올림) |
| `Assets/GameData/Balance/bills.csv` | `loan_daily_cut_min`·`loan_daily_cut_max` | 일일 징수율을 빌린 금액에 비례해 보간하는 범위(`CalculateDailyCut`) |
| `Assets/GameData/Balance/bills.csv` | `loan_cooldown_days` | 완제 후 재대출 금지 일수(`IsLoanOnCooldown`) |
| `Assets/GameData/Balance/bills.csv` | `loan_max_concurrent` | 동시에 유지할 수 있는 대출 건수 |

## 검증

### 지갑 초기화 (2026-09-18, #158)

`ManagerBootstrap`에서 `billManager.SetWalletPersistence(economyManager)`를 추가로 배선했다.
Edit Mode에서 확인했다 (Unity 6000.3.21f1, MCP로 열린 에디터에 직접 호출):

- [x] `ContractsValidationChecks.RunBatch()`: `[ContractsValidationChecks] All contract checks passed successfully.` — 4번 케이스(방어적 경로)를 유지하고, 이미 Result인 상태에서 `OnBankrupt`가 다시 발행돼도 `EndRun`을 재호출하지 않는 5번 케이스를 추가해 통과 확인
- [x] `BankruptcyChecks.RunBatch()`: `[BankruptcyChecks] PASS 9 checks.` (기존 8건 + 신규 1건) — 파산 후 `FakeWalletPersistence.RestoreWallet`이 정확히 1회, `balance=0`·`remainderText="0"`으로 호출됨을 확인
- [x] `ValidationRunner.RunAll()` 전체 하네스: 오류 0건. `BillManagerChecks`(24건)·`CoinWalletChecks`(11건)·`EconomyManagerChecks`(8건) 등 기존 검증에 회귀 없음

**미검증**: Play Mode. `ManagerBootstrap`의 배선은 `RuntimeInitializeOnLoadMethod`라 Play Mode에서만
실행된다. 막고 있던 #164(`BillManager.EndRun()` 을 부르는 곳이 없음)는 풀렸지만, 아래 #164 의
Play Mode 확인은 이 배선이 들어오기 **전**에 돈 것이라 그 회차를 근거로 삼을 수 없다.
확인하려면 크리처를 때려 코인을 모은 뒤 마감을 넘겨 파산시키고 잔액이 0 이 되는지 봐야 한다.

### 파산 (2026-09-18, #30)

Edit Mode 에서 `BankruptcyChecks.RunBatch()` 로 확인했다 (**8건 PASS**, #158에서 9건으로 증가).
`ValidationRunner.RunAll()`(#159)이 `*Checks` 를 리플렉션으로 모으므로 별도 등록 없이 전체
하네스와 함께 돈다. `BillManagerChecks` 회귀(24건)도 통과한다.

- [x] 마감 전날까지는 파산하지 않는다
- [x] 마감 당일이 미납으로 끝나면 파산하고, `OnBankrupt` 가 정확히 1회 나간다
- [x] 마감 전에 내면 파산하지 않고, 다음 날 새 청구서도 기한 안이면 파산하지 않는다
- [x] 청구서가 없으면 판정하지 않는다
- [x] 파산 후 날짜·청구서·대출·퍼크 후보가 새 회차 값이다
- [x] **단계가 1단계로 돌아간다** (`IStageService.RestoreStage(0)`)
- [x] 다음 런이 1일차 1단계 청구서를 새로 발행한다
- [x] `BalanceData` 가 dirty 되지 않는다

검증이 실제로 잡는지도 확인했다. `RestoreStage(0)` 을 빼 보니
"파산이 단계를 1단계로 되돌리지 않았습니다: -1" 로 실패했다.

### Play Mode — 하루가 실제로 흐른다 (2026-09-18, #164)

배선이 붙어 **처음으로 게임에서 확인했다.** `Game` 씬 Play 후 스태미나 소진을 발행해 런을
끝내고 씬을 다시 로드하는 식으로 날짜를 넘겼다.

| 단계 | 관찰 |
|---|---|
| 런 시작 | `Running` · 1일차 · **1단계 청구서 발행** (금액·기한은 `stages.csv`) |
| 런 종료 | `Result` 전이 · `OnDayEnded(1)` 발행 |
| 재도전 | 2일차 · 남은 일수 하나 줄어듦 — 하루가 정확히 하나씩 |
| 반복 | 마감 당일까지 하루씩 진행 (`due_days` 만큼) |
| 마감 당일 미납 종료 | **1일차로 되돌아가고 청구서가 사라짐** — 파산 발동 |
| 파산 재시작 | 1일차 · **1단계 청구서 재발행** · 단계 인덱스 0 |

오류·경고 0건. **마감 경계가 Edit Mode 와 같다** — 남은 일수 1(마감 당일) 종료에서 파산하고,
2 일 때는 무사하다.

**미검증**: 파산 시 지갑 초기화(#158)는 이 회차로 확인하지 못했다. 이 Play Mode 확인은 #158
배선이 들어오기 전에 돌았고, 번 돈도 0 이라 초기화 전후의 차이가 보이지 않는다 — 크리처를
실제로 때려 코인을 모은 뒤 파산시켜야 한다.

### 청구서·납부·대출 (2026-09-17, #27·#28·#29)

Unity 6000.3.21f1 헤드리스 배치 실행, 2026-09-17.

- [x] 컴파일: `unity run . -- -executeMethod NCAIClicker.EditorTools.BillManagerChecks.RunBatch -logFile -` → 도메인 리로드 포함 정상 종료, `error CS` 0건
- [x] `BillManagerChecks.RunBatch()`: `[BillManagerChecks] PASS 15 checks.` — 기존 6건(#27, 위 최초 작성 시점 내용) + 신규 9건(#28): `TryPay` 성공 시 코인 차감(`IEconomyService.TrySpendCoin` 경유)·`OnBillPaid` 발행·`_activeBill` 해제, 잔액 부족 시 `TryPay` 실패, 이미 낸 청구서 재납부 실패, `TryPay` 성공 시 `perks.csv` 4종 중 3개를 중복 없이 뽑아 `OnPerkOffered` 발행, 후보에 없는 id로 `TryChoosePerk` 실패(후보 목록·`OnPerkChosen` 불변), 후보 중 하나로 `TryChoosePerk` 성공(`OnPerkChosen` 발행·후보 목록 비움), 이미 고른 뒤 재선택 실패
- [x] `EconomyManagerChecks.RunBatch()` 회귀: `[EconomyManagerChecks] PASS 8 checks.` — `IBillService` 확장(`ActiveBill`/`OfferedPerkIds`/`TryChoosePerk` 추가)으로 `FakeBillService`를 고친 뒤에도 기존 배선·대출 징수 검증이 깨지지 않음을 확인
- [x] (#29) `BillManagerChecks.RunBatch()`: `[BillManagerChecks] PASS 23 checks.` — 열린 에디터에서 MCP로 직접 호출(헤드리스 배치 아님), 2026-09-17. 기존 14건(#27·#28 — 대출 스텁 전제 1건은 실제 동작 검증으로 대체) + 신규 9건(`RunLoanChecks`): 첫 청구서에서 대출 잠김, 두 번째 청구서에서 해금, 청구서 금액 초과 거부, 전액 대출 성공(원금이 `AddLoanPrincipal`로 입금·징수율이 상한), 동시 1건 거부, 잔액 부족 시 상환 실패(대출·징수율 불변), 상환 성공(이자 포함 금액 차감·징수율 0), 완제 직후 쿨다운 거부, 쿨다운 경과 후 재대출 성공(소액이라 징수율이 하한 쪽)
- [x] (#29) `EconomyManagerChecks.RunBatch()` 회귀: `[EconomyManagerChecks] PASS 8 checks.` — `LoanDailyCut`이 실제 값을 돌려주게 된 뒤에도 기존 징수 배선이 그대로 통과함을 확인
- [ ] (#29) 대출을 실제로 쓰는 플레이 흐름 — `TryTakeLoan`을 부르는 UI가 아직 없어 Play Mode 미검증(알려진 한계 참고)
- [x] `BillHud.prefab` 생성(#27): 임시 `-executeMethod` 스크립트(`TempBillHudPrefabBuilder.Build`, 실행 후 삭제)로 `PrefabUtility.SaveAsPrefabAsset` → `[TempBillHudPrefabBuilder] 저장 완료` 로그 확인, 정상 종료
- [x] `perks.csv` → `NCAI > 밸런스 CSV 임포트`로 `BalanceData.asset` 재생성, 임포터 검증(4종 고정·`id` 중복 없음·`CoinGainBoost`만 `duration_sec > 0`) 통과
- [x] 에디터 Play Mode에서 퍼크 선택 UI 확인 — #92 에서 화면이 생겨 확인했다(라이브 `TryPay` 호출로 후보를 띄웠다). HUD 프리팹을 `Game.unity`에 배치하는 것은 여전히 코어 플레이 모듈 담당 몫이다
- [x] `GameManager`가 `BeginRun`/`EndRun`을 실제로 호출하는 배선 — #164 에서 `IRunScoped` 를 구현해 붙였고, Play Mode 로 하루 진행과 파산 발동을 확인했다 (위 "Play Mode" 절)

## 마감 미납과 파산 (#30)

**미납 = 즉시 파산.** 유예·부분 납부·추심 삭감·반액 정산은 없다 (#6 에서 확정, 근거는
REFERENCE_ANALYSIS 6절). 대출로 코인을 만드는 것이 유일한 회피 수단이고, 그것도 마감 전에
`TryPay` 로 실제 납부까지 끝내야 한다.

```
EndRun()  → OnDayEnded 발행
          → 청구서가 있고 · 미납이고 · 오늘이 마감일 당일이거나 그 뒤면
          → OnBankrupt 발행 → 회차 초기화
```

### 마감일은 낼 수 있는 마지막 날이다

`DueDay` 당일에 런이 끝나면서 미납이면 **그 시점에 파산**이다. 하루를 더 넘겨야 파산하는
것이 아니다. 하루의 끝에서만 판정하므로 "마감 판정 전에 납부 기회를 제공한다"(ARCHITECTURE)는
자연히 지켜진다 — 런 내내 `TryPay` 가 열려 있다.

검증을 쓰면서 이 경계를 반대로 잡았다가 틀렸다. `DaysLeft == 1`(마감 당일)에서 이미 마지막
기회를 쓴 것이다.

### 런 경계에 붙기까지 (#164)

**한동안 이 판정은 게임에서 실행되지 않았다.** `BillManager` 가 `BeginRun`/`EndRun` 메서드는
가졌는데 `IRunScoped` 를 선언하지 않아 `GameManager` 의 `GetComponentsInChildren<IRunScoped>`
에 잡히지 않았다. 1.16(#111)이 런 경계를 배선할 때 구현체만 모았고, 이 클래스는 이름만 같아
빠졌다 — **하루 진행·청구서 발행·대출 징수·파산 판정이 전부 잠들어 있었다.**

각 기능의 Edit Mode 검증은 `BeginRun`/`EndRun` 을 직접 불러 전부 통과했다. **부르는 주체가
없다는 것은 아무 검증도 보지 않았다.** 그래서 #164 가 배선 자체를 보는 `RunWiringChecks` 를
함께 만들었다.

#164 에서 인터페이스를 붙였는데 한 줄로 끝나지 않았다. **`StageGoalManager` 가 `BillManager`
보다 먼저 불려야 한다** — `EndRun` 에서 앞은 목표를 채웠으면 단계를 올리고 뒤는 파산이면
단계를 0 으로 되돌린다. 순서가 뒤집히면 **파산인데 단계가 올라간다.** `GetServiceOrder` 는
클래스 이름으로 순번을 매기는데 둘 다 기타(10)라 동점이었고, `Array.Sort` 는 동점 순서를
보장하지 않는다. `Stage`=5 · `Bill`=6 을 줘서 고정했다.

### `OnBankrupt` 로 Result 로 전이한다는 계약은 이 경로에서 늦다

ARCHITECTURE 3절은 "`OnBankrupt` 는 GameManager 만 구독해 Result 로
전이시킨다"고 적었지만, `EndRun()` 은 **Result 전이 도중에** 불리게 된다. 발행 시점에는 이미
Result 이므로 `GameManager.HandleRunEnded` 의 `CurrentState == Running` 검사에 걸려 아무 일도
하지 않는다.

**파산은 런을 끝내는 원인이 아니라 끝난 런의 결과다.** 마감을 놓치는 순간이 곧 하루의 끝이라
런 도중에 파산이 발생할 길이 없다. 그래서 `OnBankrupt` 는 "이 회차가 파산으로 끝났다"는
통지로 쓴다. `ARCHITECTURE.md` "하루 종료 순서"의 문구를 이 뜻에 맞게 정리했고,
`ContractsValidationChecks` 에도 이미 Result 인 상태에서 `OnBankrupt` 가 다시 발행돼도
`EndRun` 을 재호출하지 않는다는 케이스를 추가했다 ([#158](https://github.com/KDNA-Gwangju-1/NCAIClicker/issues/158)).

### 무엇을 되돌리나

| 대상 | 초기화 | 비고 |
|---|---|---|
| 날짜 | 1일차 | `_hasBegun` 을 내려 다음 `BeginRun` 이 날짜를 올리지 않게 한다 |
| 청구서 순번 | 1부터 | `_billIndex`. #150 이후 이 필드는 단계가 아니라 **누적 발행 순번**이고 대출 해금(`loan_unlock_bill_index`)의 기준이라 새 회차에서 다시 세야 한다 |
| 활성 청구서 | 버림 | 다음 `BeginRun` 이 1단계 청구서를 새로 낸다 (ARCHITECTURE "게임 시작과 파산 재시작에도") |
| 대출·재대출 쿨다운 | 없음으로 | |
| 퍼크 후보 | 비움 | |
| 코인·소수 잔여 | **0 으로 초기화** | `IWalletPersistence` 소비자에 `BillManager` 를 추가해 열었다 (#158) |
| **영구 업그레이드** | **유지** | 회차를 넘겨 남는 유일한 성장이다 |
| **단계** | 1단계로 | `IStageService.RestoreStage(0)`. #150 이 단일 출처를 열어 주었고 그 주석이 "저장 복원 및 **파산 처리용**"으로 이 자리를 가리킨다 |

## 알려진 한계

- ~~`GameManager`가 `BillManager.BeginRun()`/`EndRun()`을 호출하지 않는다.~~ — #164 에서 `IRunScoped` 를 구현해 붙였다. Play Mode 로 하루 진행·청구서 발행·파산 발동을 확인했다 (위 검증).
- ~~단계 진행(`_billIndex`)을 `BillManager`가 자체 순번으로 관리한다.~~ — #150 에서 `IStageService` 단일 출처 연결로 해결됐다. 청구서 금액과 기한은 현재 단계를 따르고, `_billIndex` 는 대출 해금 등에서 쓸 누적 발행 순번으로만 쓰인다.
- ~~파산 판정(#30)이 없어 청구서를 기한 내에 내지 않아도 아무 일이 일어나지 않는다.~~ — #30 에서 붙였다. 함께 남았던 셋 중 지갑 초기화는 #158 이 해결했고 아래 둘이 남는다.
- **결과 화면에 "파산"이 뜨지 않는다.** `OnBankrupt` 를 받아 표시할 화면(6.2/#34)이 아직 없다. #34 가 이 카드를 선행으로 잡고 있어 순환이었고, 발행까지가 #30 의 몫이다.
- **파산 결과가 저장되지 않는다.** `SaveData.WasBankrupt`·`LastCompletedDay` 필드는 있지만 채우는 곳이 없다. `SaveManager` 를 부르는 곳이 `GameManager.StartNewRun()` 하나뿐이고 거기서 `new SaveData()` 빈 객체를 쓴다 — 매니저 상태가 전혀 담기지 않는다.
- ~~**파산해도 돈이 그대로 남는다 — 페널티가 약하다.**~~ — #158 에서 해결. `IWalletPersistence`
  소비자에 `BillManager` 를 추가해(A안) 파산 시 `RestoreWallet(0, "0")` 으로 코인·소수 잔여를
  비운다. 영구 업그레이드는 여전히 유지된다.
- **파산 즉시 상태를 되돌리는 것이 임시 방편이다.** 원래는 결과 화면을 보여 준 뒤 새 회차를 시작할 때 되돌리는 것이 맞지만, 새 회차 시작이 매니저 상태를 초기화하지 않아(`DontDestroyOnLoad`) 지금은 여기서 되돌리지 않으면 1일차 재시작이 성립하지 않는다.
- 대출을 부르는 UI가 없다. `TryTakeLoan`의 금액은 호출측이 정하고 이 클래스는 활성 청구서 금액을 넘지 못하게만 막는다 — 얼마를 빌릴지 고르는 화면은 아직 없다.
- 대출 상태(`ActiveLoan`·`LastLoanRepaidDay`)가 저장·복원되지 않는다. `SaveData`에 필드는 이미 있지만 `IBillService`에 복원 통로가 없어 재실행하면 빚이 사라진다 — 공용 계약 변경이라 별도 이슈로 발의한다.
- 징수는 수입이 들어올 때만 일어난다. 하루 종일 한 푼도 벌지 못하면 뜯기는 것도 없다 — 원작이 그러한지는 7.2 실측에서 확인한다.
- `BillManager`의 날짜·청구서·퍼크 후보(`OfferedPerkIds`) 상태는 저장/복원되지 않는다(`SaveManager` 미연동). ARCHITECTURE.md는 `SaveData.OfferedPerkIds`/`PendingPerkIds` 필드를 이미 계약해 뒀으므로, 저장 연동은 그 필드에 채워 넣는 방식으로 붙이면 된다.
- `BillHud.prefab`은 **쓰지 않는다.** #33(6.1 인게임 HUD)이 만든 `Assets/Prefabs/UI/GameHud.prefab`이
  같은 `BillHud` 컴포넌트를 품은 채 `Game.unity`에 배치됐다. 둘 다 올리면 캔버스와 청구서 라벨이
  두 개가 된다. 정리 여부는 #27 담당과 정한다 — 상세는 [ingame-hud.md](ingame-hud.md).
- ~~퍼크 선택(`OnPerkChosen`)의 실제 게임플레이 효과 적용이 없다.~~ — #126 에서 네 소유자(`StaminaManager`·`EconomyManager`·`HammerSwingController`·`CreatureManager`)가 `OnPerkChosen` 을 구독해 스스로 적용한다. 적용 시점 규칙과 한계는 [퍼크 효과](perks.md).
- ~~퍼크 선택 UI가 없다.~~ — #92 에서 붙였다. "선택 중 게임 시계 정지"도 함께 들어갔다
  ([퍼크 3장 선택 화면](perk-choice-ui.md)). 다만 **`TryPay` 를 부르는 곳이 없어 정상 플레이로는
  이 화면이 뜨지 않는다** — 납부 UI 는 6.10([#181](https://github.com/KDNA-Gwangju-1/NCAIClicker/issues/181))이다.

## 갱신 이력

| 날짜 | 이슈 | 누가 | 무엇이 바뀌었나 |
|---|---|---|---|
| 2026-09-17 | #27 | Claude | 최초 작성 — `BillManager`/`BillHud` 구현, `OnBillDueSoon` 발행 연결 |
| 2026-09-17 | #28 | Claude | `TryPay` 조기 납부 구현, `perks.csv`/`PerkType`/`PerkDef` 추가, 납부 성공 시 퍼크 3종 제시(`OnPerkOffered`)·선택(`TryChoosePerk`, `OnPerkChosen`) |
| 2026-09-17 | #29 | Claude | 대출 구현 — 해금 순번·청구서 금액 한도·이자 올림·금액 비례 징수율·동시 1건·재대출 쿨다운. `BillManagerChecks`에 `RunLoanChecks` 추가 |
| 2026-09-18 | #150 | saltlake00 | `IStageService` 연결 — 자체 단계 순번을 걷어내고 단일 출처의 현재 단계로 청구서 발행 |
| 2026-09-18 | #30 | twins6375-art | 마감 미납 파산 판정·`OnBankrupt` 발행·회차 초기화(단계 포함). `BankruptcyChecks` 신규. **지갑 초기화는 공용 계약에 막혀 제외** |
| 2026-09-18 | #158 | hunil58 | `IWalletPersistence` 소비자에 `BillManager` 추가(A안) — 파산 시 코인·소수 잔여를 0으로 초기화. `ARCHITECTURE.md` "하루 종료 순서"의 `OnBankrupt` 문구를 "결과 통지"로 정정, `ContractsValidationChecks` 5번 케이스 추가, `BankruptcyChecks`에 지갑 초기화 검증(`FakeWalletPersistence`) 추가 |
| 2026-09-18 | #164 | twins6375-art | `BillManager` 가 `IRunScoped` 를 구현해 런 경계에 붙음 — 4.1~4.4 가 처음으로 실제 동작한다. `GetServiceOrder` 순번 부여, `RunWiringChecks` 신규, Play Mode 확인 |
| 2026-09-18 | #33 | yahoo-afk | `BillHud`에 마감 임박 강조(`_emphasisDaysLeft`, 기본 2)와 `OnBillPaid` 구독 추가, `OnEnable`에서 `IBillService.ActiveBill`로 금액까지 조회. `GameHud.prefab`이 `BillHud.prefab`을 대체 — [ingame-hud.md](ingame-hud.md) |
| 2026-09-18 | #92 | twins6375-art | 퍼크 선택 화면이 붙어 관련 한계를 닫았다. 납부 통로가 없다는 사실(`TryPay` 호출처 0)을 한계에 명시 |
