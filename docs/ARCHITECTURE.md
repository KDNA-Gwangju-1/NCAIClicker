# 아키텍처 및 프로파일링 계획

5인 7일 규모에서는 DI 프레임워크나 BT(비헤이비어 트리) 같은 범용 솔루션이 오히려 구현·디버깅 비용을 늘린다. 싱글톤 매니저 + 정적 이벤트 버스, 타격 대상은 FSM으로 충분하다. 프로파일링도 상시가 아니라 작업 7.1·7.4 두 체크포인트로 한정한다.

작업 1.2.1 에서 아래 인터페이스 시그니처와 이벤트를 팀 전체가 확인하고 고정한다. 이후 임의 변경 시 별도 이슈를 먼저 발의한다 ([EXECUTION_PLAN.md](EXECUTION_PLAN.md) 에이전트 작업 규칙 참고).

## 0. 씬 구성

씬은 **2개**다. 빌드 순서도 이 순서다.

| # | 씬 | 역할 | 소유자 |
|---|---|---|---|
| 0 | `MainMenu` | 타이틀, 업그레이드 구매, 단계 표시, 시작 버튼 | UI·연출 |
| 1 | `Game` | 인게임. 결과 화면은 이 씬 안의 UI 패널로 처리한다 | 코어 플레이 |

**나눈 이유는 병합 충돌이다.** 씬 파일은 여러 명이 동시에 고치면 병합이 가장 어려운 파일이다. 메뉴를 만드는 사람(5번)과 인게임을 만드는 사람(2번)이 서로 다른 씬을 가지면 애초에 부딪힐 일이 없다.

**결과 화면을 별도 씬으로 만들지 않는 이유**는 반대다. 결과 화면은 이번 런의 획득 코인·청구서 내역을 그대로 받아 써야 하는데, 씬을 넘기면 그 데이터를 전달할 통로를 따로 만들어야 한다. 같은 씬의 패널이면 그냥 읽으면 된다.

### 매니저의 생존 — 부트스트랩 씬을 두지 않는 이유

매니저 초기화 전용 씬(`Bootstrap`)을 따로 두는 방식도 있지만 **쓰지 않는다.**

이유는 개발 중에 반드시 부딪히는 문제 때문이다. 개발자는 하루에도 수십 번 `Game` 씬만 열어놓고 Play 를 누른다. 부트스트랩 씬 방식이면 그때마다 매니저가 하나도 없어서 `NullReferenceException` 이 쏟아지고, 결국 "어느 씬에서 Play 해도 매니저가 생기게 하는 장치"를 또 만들게 된다. **그 장치를 만들면 부트스트랩 씬은 할 일이 없어진다.** 그래서 처음부터 장치 쪽만 남긴다.

매니저 프리팹을 `RuntimeInitializeOnLoadMethod` 로 씬 로드 전에 자동 생성하고 `DontDestroyOnLoad` 로 유지한다. MainMenu ↔ Game 을 오갈 때 다시 만들지 않는다. 구현은 PM·통합 담당이 한다 (작업 1.2.3).

이렇게 하면 관리할 씬이 하나 줄고, 아무 씬이나 열어서 바로 Play 할 수 있다.

### 씬 전환 시 정적 이벤트

씬을 오가므로 3절의 구독 해제 규칙이 **반드시** 지켜져야 한다. `GameEvents` 는 정적이라 씬이 바뀌어도 구독이 남고, Game 씬을 두 번째로 들어가는 순간 코인이 두 배로 들어오는 식의 버그가 난다.

## 1. 매니저 구조 — 출발점이지 확정이 아니다

싱글톤 매니저 + 정적 이벤트 버스를 쓴다. DI 프레임워크(Zenject·VContainer)는 배우고 세팅하는 데만
하루 이상 들어 7일 일정에 맞지 않는다.

> **아래 목록은 출발점이다.** 매니저를 몇 개로 쪼갤지, 싱글톤으로 둘지 정적 클래스로 둘지는
> **구현자가 정한다.** `SaveManager` 는 함수 두 개짜리 정적 클래스여도 되고, `AudioManager` 를
> `UIManager` 안에 두어도 된다. 팀이 실제로 충돌하는 지점은 매니저 개수가 아니라
> **이벤트 이름·인터페이스 시그니처·배율 적용 위치**이고, 고정하는 것은 그 셋뿐이다.

```
GameManager      런 상태 머신 (MainMenu → Running → Result)
                 Result 진입 사유를 구분한다: StaminaDepleted / Bankrupt
StaminaManager   스태미나 소모/회복
BillManager      하루 진행, 청구서 마감, 대출과 징수, 파산 판정
PiggyManager     타격 대상 스폰, 개별 FSM 구동
EconomyManager   코인, 업그레이드 비용/레벨 계산
FeverManager     피버 게이지, 배율
SaveManager      SaveData DTO 직렬화
UIManager        HUD/화면 전환
AudioManager     SFX/BGM
```

### 초기화 순서 (이건 지킨다)

매니저를 어떻게 쪼개든 **이 의존 순서는 지켜야 한다.** 어긋나면 EconomyManager 가 저장된 코인을
읽기 전에 0으로 초기화하는 식의 버그가 난다.

```
1. 저장 로드        (다른 모든 초기값의 출처)
2. 코인·업그레이드 레벨 복원
3. 스태미나 / 피버 / 청구서 / 타격 대상
4. UI / 오디오      (위 상태를 구독해 화면에 반영)
5. 런 상태 머신     (마지막에 MainMenu 상태로 진입)
```

0절에서 정한 대로 **부트스트랩 씬은 두지 않는다.** 매니저 프리팹을 `RuntimeInitializeOnLoadMethod`
로 씬 로드 전에 생성하고 `DontDestroyOnLoad` 로 유지한다 (작업 1.2.3).

### 모듈 매핑

| 매니저 | 모듈 |
|---|---|
| GameManager, PiggyManager, StaminaManager | 코어 플레이 |
| EconomyManager, BillManager, SaveManager | 성장·저장 |
| FeverManager | 피버·보너스 |
| UIManager, AudioManager | UI·연출 |

**실제 담당자의 정본은 GitHub 이슈의 Assignee 다.** 이 표는 카드가 비어 있을 때 우선권이 있는
모듈 오너를 가리킬 뿐, 다른 사람이 그 카드를 집는 것을 막지 않는다.

## 2. 공용 인터페이스

2026-09-16 초기 세팅 정정(#46). 아래는 **구현 전 계약**이며 현재 게임 매니저가 구현됐다는 뜻이 아니다.
다른 매니저 구현 클래스를 직접 참조하지 않는다. 요청·조회는 아래 인터페이스로, 상태 변화는 3절 이벤트로 전달한다.

```csharp
public enum HitSource { Hover, AutoHammer }

public readonly struct HitInfo
{
    public HitSource Source { get; }
    public float Damage { get; }       // 내구도를 깎는 양. 코인과 무관
    public Vector3 WorldPos { get; }
    public HitInfo(HitSource source, float damage, Vector3 worldPos)
    {
        Source = source;
        Damage = damage;
        WorldPos = worldPos;
    }
}

public interface IHittable
{
    void OnHit(HitInfo info);
    bool IsAlive { get; }
}

public readonly struct BreakInfo
{
    public string TargetId { get; }
    public decimal RawCoin { get; }    // 최대 hp × coin_mult + break_bonus
    public float StaminaRestore { get; }
    public Vector3 WorldPos { get; }
    public BreakInfo(string targetId, decimal rawCoin, float staminaRestore, Vector3 worldPos)
    {
        TargetId = targetId;
        RawCoin = rawCoin;
        StaminaRestore = staminaRestore;
        WorldPos = worldPos;
    }
}

public interface IBillService
{
    int CurrentDay { get; }
    int DaysLeft { get; }              // max(0, DueDay - CurrentDay + 1)
    float LoanDailyCut { get; }        // 대출이 없으면 0
    Bill ActiveBill { get; }           // 마감 전 청구서. 없으면 null
    string[] OfferedPerkIds { get; }   // 납부 직후 골라야 할 퍼크 후보 3개. 고르면 비워진다
    bool TryPay(Bill bill);
    bool TryTakeLoan(long amount);     // 두 번째 청구서부터, 동시 1건
    bool TryRepayLoan();               // 전액 상환. 재대출 쿨다운 시작
    bool TryChoosePerk(string perkId); // OfferedPerkIds 중 하나를 고른다. 목록에 없으면 false
}

public interface IEconomyService
{
    void AddCoin(decimal rawAmount);   // 파괴 보상만. 배율·징수 전 값
    void AddLoanPrincipal(long amount);// 원금 입금. 배율·징수·RunCoin 집계 제외
    bool TrySpendCoin(long amount);
    long CurrentCoin { get; }
    long RunCoin { get; }              // 이번 런 순수입의 정수 부분. 지출·대출 제외
}

// 런 경계. 부르는 쪽은 GameManager 뿐이다 (이슈 #71, #111).
// 구현체는 Managers 프리팹 안팎에 모두 있다 — 씬에 사는 구현체는
// GetComponentsInChildren 으로 잡히지 않아 GameManager 가 따로 모은다 (이슈 #126).
public interface IRunScoped
{
    void BeginRun();
    void EndRun();
}

// 저장 복원. SaveManager 만 쓴다 (이슈 #71)
public interface IWalletPersistence
{
    void RestoreWallet(long balance, string remainderText);
    string CurrentRemainderText { get; }
}

// 업그레이드 실효값 조회. 소비처는 BalanceData 기준값 대신 이것을 읽는다 (이슈 #116).
// 기준값은 항상 호출측이 넘긴다 — spawn_count 처럼 기준값이 현재 단계(StageDef)에 따라
// 달라지는 스탯이 있어, 표를 여기서 복제하지 않도록 통일했다 (#24 구현 중 발견, #116 코멘트).
public interface IUpgradeStats
{
    float GetStat(StatId stat, float baseValue);
}

// 업그레이드 구매. 메뉴·결과 화면(작업 6.8)이 쓴다 (이슈 #116).
public interface IUpgradeShop
{
    int GetLevel(string upgradeId);
    long GetNextCost(string upgradeId);
    bool TryPurchase(string upgradeId);
}

// 업그레이드 레벨 저장 복원. SaveManager 만 쓴다 (이슈 #116).
public interface IUpgradePersistence
{
    void RestoreUpgradeLevels(int[] levelsBySortOrder);
    int[] CurrentUpgradeLevels { get; }
}

public interface ISaveService
{
    SaveData Load();
    void Save(SaveData data);
    bool HasSave { get; }              // 저장 파일 존재 여부. Load()는 없어도 항상 기본값을 반환한다 (이슈 #139)
}

// MainMenu 버튼이 씬 전환을 요청하는 계약. GameManager만 구현한다 (이슈 #142)
public interface IGameFlowService
{
    void StartNewRun();
    void ContinueRun();
    void QuitGame();
}

// 단계 진행 상태 조회 계약. StageGoalManager 가 구현하고 CreatureManager 와 BillManager 가 소비한다 (이슈 #150)
public interface IStageService
{
    int CurrentStageIndex { get; }
    int CurrentStageNumber { get; }
    bool IsGoalReached { get; }
    bool IsMaxStage { get; }
    bool AdvanceStage();
    void RestoreStage(int stageIndex);
}

[Serializable]
public class Bill
{
    public long Amount;
    public int IssuedDay;
    public int DueDay;                 // IssuedDay + 기한 - 1, 이 날 종료 전에 납부
    public bool IsPaid;
}

[Serializable]
public class Loan
{
    public long Principal;
    public long Owed;
    public float DailyCut;             // 대출 시 1회 결정. 미상환 기간에 고정
}

public enum ResumePoint { MainMenu, Result, PerkSelection }

[Serializable]
public class SaveData
{
    public const int CurrentVersion = 2; // SaveManager도 이 상수를 참조한다. 숫자를 두 곳에 적지 않는다
    public int Version = CurrentVersion;
    public long TotalCoin;
    public string CoinRemainder = "0"; // decimal을 InvariantCulture 문자열로 저장
    public int StageIndex;             // 배열 인덱스: 0부터. StageDef.Stage는 1부터
    public long BestRunCoin;
    public int[] UpgradeLevels;        // upgrades.csv sort_order 순
    public int CurrentDay = 1;
    public int BillIndex = 1;
    public bool HasActiveBill;         // 저장 파일 전용 플래그. 메모리상에서는 ActiveBill == null로만 판단
    public Bill ActiveBill;
    public bool HasActiveLoan;         // 저장 파일 전용 플래그. 메모리상에서는 ActiveLoan == null로만 판단
    public Loan ActiveLoan;            // 없으면 null
    public int LastLoanRepaidDay = -1;  // 대출 객체를 지워도 쿨다운 유지
    public ResumePoint ResumePoint;
    public long LastRunCoin;
    public int LastCompletedDay;
    public bool WasBankrupt;
    public bool IsCompleted;
    public string[] OfferedPerkIds;    // 선택 화면을 다시 열어도 같은 후보
    public string[] PendingPerkIds;    // 결과 화면에서 선택한 다음 런 효과
}
```

`EconomyManager.SetBillService(IBillService)` 는 어느 인터페이스에도 넣지 않는다. 서비스 계약이 아니라
매니저를 조립(wiring)하는 통로이기 때문이다 — 조립하는 지점(ManagerBootstrap 또는 GameManager 초기화)
한 곳만 구현 클래스를 알고, 그 뒤의 상호작용은 `IRunScoped`·`IWalletPersistence` 로만 한다 (이슈 #71).

### 저장 경계

- 날짜·청구서·대출·쿨다운·퍼크 후보·선택 대기·소수 잔여를 **하나의 스냅샷**으로 저장한다.
- 메뉴/결과 화면에서 구매·납부·대출·상환·퍼크 선택을 완료한 직후와 런 시작 직전에 저장한다.
- 런 도중 종료하면 **그 런의 시작 스냅샷**으로 복귀한다. 그날의 수입·지출·납부·대출·퍼크 변경을 전부 함께 되돌린다. 씬의 대상 위치·남은 내구도는 저장하지 않는다. 중간 상태 일부만 저장해 재실행으로 빚만 지워지는 일을 막는다.
- 하루 종료 처리가 끝나면 결과와 다음 행동 상태를 함께 저장한다. 로드 시 `LastCompletedDay`를 다시 정산하지 않는다.
- 저장은 임시 파일 작성 후 교체한다. JSON 오류·지원하지 않는 버전은 원본을 백업하고 경고 후 초기화한다. 버전 1은 회차 정보가 없으므로 성장·코인은 유지하고 하루/청구서/대출을 기본값으로 보완한다.
- 파산 시 보유 코인·소수 잔여·단계·날짜·청구서·대출·퍼크를 새 회차 값으로 초기화한다. 영구 업그레이드와 최고 기록은 유지한다. 파산 결과는 `WasBankrupt`와 `LastCompletedDay`로 별도 표시한다.

### 직렬화 방식 (이슈 1.2.2)

- 직렬화기는 Unity 내장 `JsonUtility`를 쓴다. Newtonsoft 등 추가 패키지를 넣지 않는다 (패키지 의존성을 늘리지 않는다는 PATTERNS.md 8절 기조와 동일).
- 저장 파일은 `Application.persistentDataPath/save.json` 하나만 쓴다. 슬롯을 나누지 않는다.
- 버전 정책: 필드를 추가·제거하거나 의미를 바꿀 때마다 `Version`을 1 올린다. `SaveManager.Load()`는 저장된 `Version`으로 분기해 예전 필드를 오늘 구조로 채워 넣는다 (버전 1→2 사례는 위 저장 경계 참고). 마이그레이션 분기는 지우지 않고 버전 수만큼 누적한다.
- `ActiveBill`·`ActiveLoan`처럼 없을 수 있는 참조 필드는 **메모리상에서는** `null`로 둔다.
  ~~`JsonUtility`는 `null` 참조 필드를 `null`로 직렬화·역직렬화한다~~ — **틀렸다.** 구현 중
  저장 → 로드 왕복 테스트로 확인한 결과, `JsonUtility`는 참조 타입 필드의 `null`을 표현하지
  못하고 필드 기본값으로 채운 빈 객체로 되살린다(이슈 #25, #76). 그래서 `SaveData`에
  `HasActiveBill`·`HasActiveLoan` bool 플래그를 저장 파일 전용으로 추가했다.
  `SaveManager.Save()`는 저장 직전 `ActiveBill`/`ActiveLoan`의 null 여부를 이 플래그에 반영하고,
  `SaveManager.Load()`는 역직렬화 직후 플래그가 `false`인 참조를 다시 `null`로 되돌린다.
  이 변환은 `SaveManager` 안에서만 일어나므로 다른 매니저는 여전히 `ActiveBill == null`
  판단만 쓰면 된다.
- 예시 JSON (필드 순서는 위 코드 선언 순. `ActiveLoan`이 없는 상태라 `HasActiveLoan`이
  `false`이고 `ActiveLoan`은 필드 기본값으로 채워진 빈 객체로 저장된다 — 그래도 로드하면
  `SaveManager`가 다시 `null`로 되돌린다):

```json
{
  "Version": 2,
  "TotalCoin": 15420,
  "CoinRemainder": "0.37",
  "StageIndex": 2,
  "BestRunCoin": 980,
  "UpgradeLevels": [3, 1, 0, 2],
  "CurrentDay": 5,
  "BillIndex": 2,
  "HasActiveBill": true,
  "ActiveBill": { "Amount": 500, "IssuedDay": 3, "DueDay": 7, "IsPaid": false },
  "HasActiveLoan": false,
  "ActiveLoan": { "Principal": 0, "Owed": 0, "DailyCut": 0.0 },
  "LastLoanRepaidDay": -1,
  "ResumePoint": 0,
  "LastRunCoin": 210,
  "LastCompletedDay": 4,
  "WasBankrupt": false,
  "IsCompleted": false,
  "OfferedPerkIds": ["perk_pay_early", "perk_double_hit"],
  "PendingPerkIds": []
}
```

### 코인 계산·정산 계약

1. 호버/자동 망치는 `HitInfo.Damage`만 전달한다. 대상의 현재 내구도는 `float`로 계산하여 작은 자동 망치 피해도 누적한다.
2. 대상이 살아 있음 → 파괴됨으로 바뀔 때 `OnTargetBroken`을 **한 번만** 발행한다. 원시 보상은 초기 최대 내구도로 계산하며 마지막 남은 내구도를 쓰지 않는다.
3. EconomyManager가 파괴 이벤트를 받아 `AddCoin`을 호출한다. 호출측은 피버·보너스·대출 계수를 곱하지 않는다. 같은 파괴에서 이벤트와 직접 입금을 동시에 호출하지 않는다.
4. decimal로 `rawAmount × 피버 × 보너스 × (1 - 대출 징수율)`을 계산한다. CSV의 float 배율은 곱하기 **전에** decimal로 변환한다.
5. 지갑은 `기존 소수 잔여 + 순수입`의 정수 부분만 입금하고 소수는 보관한다. `RunCoin`은 **이번 런 순수입만 별도로 합산한 뒤** 정수 부분을 표시한다. 전날 잔여, 대출 원금, 업그레이드·청구서 지출은 단계 목표에 영향을 주지 않는다.
6. `OnCoinEarned`는 이번에 지갑에 들어간 정수 증분, `OnBalanceChanged`는 지갑 현재값, `OnRunCoinChanged`는 런 순수입 정수값이다. UI는 목적에 맞는 이벤트를 쓰며 배율을 재적용하지 않는다.
7. 바닥 코인은 연출만 담당한다. 런 종료 시 연출을 회수해도 재지급하지 않는다. 소수 잔여는 회차 안에서 이월·저장하고 파산 시 버린다.
8. 대출 원금은 `AddLoanPrincipal`로 입금한다. 이자 포함 상환액은 소수 부분을 올림해 정수로 확정한다. 수입 징수는 부채를 줄이지 않는다.

### 하루 종료 순서

GameManager만 `OnStaminaDepleted`와 `OnBankrupt`를 구독해 종료 순서를 조정한다.
입력·스윙 중지 → 확정된 파괴 보상 처리 완료 → 런 목표 판정 → 마감일이면 납부/대출 선택 →
미납 확정 시 파산 → 결과 스냅샷 저장 순이다. 마감 판정 전에 납부 기회를 제공한다.
`OnDayEnded`는 날짜당 한 번만 발행한다. 다음 날 시작 때 날짜를 증가시키며,
이미 발행한 청구서의 금액·마감은 단계 상승으로 소급 변경하지 않는다.
청구서는 동시에 한 장이며, 납부 후 다음 청구서는 **다음 날 시작 시** 당시 단계값으로 발행한다.
게임 시작과 파산 재시작에도 첫 청구서를 발행한다.

## 3. 이벤트 버스

기존 공유 이름 `GameEvents.On...`은 유지한다. 발행 메서드는 `Publish...`로 구분하고 이벤트를 클래스 밖에서 직접 Invoke하지 않는다.

| 이벤트 | 인자 | 용도 |
|---|---|---|
| `OnCoinEarned` | `long` | 지갑 정수 입금 증분, 파괴 수입만 |
| `OnBalanceChanged` | `long` | 입금·지출·대출·로드 후 지갑 현재값 |
| `OnRunCoinChanged` | `long` | 런 순수입 현재값, 런 시작 시 0 |
| `OnBillIssued`, `OnBillPaid` | `Bill` | 청구서 발행/납부 |
| `OnDayEnded` | `int` | 완료된 날짜 |
| `OnBillDueSoon` | `int` | 남은 일수 |
| `OnBankrupt` | 없음 | 마감 선택 후 미납 확정 |
| `OnTargetBroken` | `BreakInfo` | 파괴 보상의 유일한 출처 |
| `OnSwingResolved` | `HitSource, bool` | 소스와 적중 여부. 정확도는 Hover만, 피버는 적중만 |
| `OnStaminaChanged` | `float, float` | 현재/최대 스태미나 |
| `OnStaminaRestored` | `float` | 실제 회복된 양. 명령이 아니므로 재회복하지 않는다 |
| `OnStaminaDepleted` | 없음 | 런 종료 요청 |
| `OnFeverGaugeChanged` | `float, float` | 현재/최대 피버 게이지 |
| `OnFeverStart`, `OnFeverEnd` | 없음 | 피버 상태 변화 |
| `OnStageGoalReached` | `int` | 단계 목표(코인) 도달, 도달한 단계 번호 |
| `OnPerkOffered` | `string[]` | 조기 납부 성공 시 뽑힌 퍼크 후보 id 3개 |
| `OnPerkChosen` | `string` | 퍼크 후보 중 고른 id |

선언은 `public static event Action<...>` 형식이다. 피버는 두 소스의 적중을 받아도 되지만,
자동 망치를 정확도 분모·분자에 넣지 않는다. UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다.
게이지 값이 실제로 바뀐 경우에만 발행하고, 지속 감소로 매 프레임 바뀌면 UI 갱신을 묶는다.

### 구독과 초기화

- 구독은 `OnEnable`, 해제는 `OnDisable`에 쌍으로 작성한다.
- `ResetAll()`은 `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)`에서 **최초 구독 전에 한 번만** 실행한다. `BeforeSceneLoad` 매니저 생성보다 앞선다.
- **런 시작·재도전·씬 전환에서는 ResetAll을 호출하지 않는다.** DontDestroyOnLoad 매니저가 계속 활성 상태이면 OnEnable이 다시 실행되지 않기 때문이다.
- 런별 수치만 별도 초기화한다. 정적 구독 목록을 런 데이터처럼 지우지 않는다.
- Enter Play Mode Options를 끄고 도메인/씬 리로드를 유지한다.

## 4. 타격 대상 AI — FSM 채택

BT(비헤이비어 트리)가 아니라 **FSM**을 쓴다.

```
Idle → Moving → (호버 감지) BeingHit → (조건 충족) Fleeing → Moving (반복)
```

**근거**: BT는 여러 목표 사이의 우선순위 판단(공격/도망/순찰 등 다중 분기)이 필요할 때 이점이 있는데, 타격 대상의 행동은 "이동 → 피격 → 경직 → 재이동"이라는 선형적 상태 전이뿐이다. BT 도입은 노드 설계·디버깅 비용만 늘리고 실익이 없다. Enum 기반 커스텀 FSM 클래스(`enum TargetState { Idle, Moving, BeingHit, Fleeing }` + `switch` 전이) 하나로 충분하며, 로직과 애니메이션 상태를 분리해두면 디자이너가 애니메이션만 건드릴 때 코드 충돌이 줄어든다.

## 5. 프로파일링 계획 — 상시 아님, 체크포인트 2회

**근거**: 7일짜리 소규모 3D 게임에서 지속적 프로파일링은 시간 낭비다. 이미 자동 망치를 "물리 추적 대신 글로벌 타이머 적중"으로 설계해 사전 최적화가 되어 있으므로, 남은 리스크는 UI/파티클 쪽 흔한 실수 정도다. 체크포인트 방식으로 충분하다.

| 시점 | 방법 | 목적 |
|---|---|---|
| 작업 7.1 (1차 통합 빌드 직후) | Unity Profiler CPU/Rendering 모듈 | Canvas 리빌드 과다, 드로우콜·그림자 캐스터 과다, `Update()` 남용 등 흔한 실수 조기 발견 |
| 작업 7.4 (배포 후보 빌드 후) | Development Build + Profiler, Frame Debugger, Memory 모듈 | 드로우콜 수, GC 스파이크(파티클·숫자 팝업 인스턴스화) 정식 점검 |

**성능 예산** (킥오프에서 팀 합의):
- 1920×1080 기준 60fps 유지
- 동시 파티클/피격 이펙트 인스턴스 상한 20개
- 드로우콜 200 이하, 화면 내 삼각형 15만 이하
- 그림자를 드리우는 오브젝트는 타격 대상만 — 파편과 코인은 그림자를 끈다
- 위 수치를 초과하면 "느낌상 괜찮다"가 아니라 이 기준으로 판단한다.
