# 아키텍처 및 프로파일링 계획

5인 7일 규모에서는 DI 프레임워크나 BT(비헤이비어 트리) 같은 범용 솔루션이 오히려 구현·디버깅 비용을 늘린다. 싱글톤 매니저 + 정적 이벤트 버스, 타격 대상은 FSM으로 충분하다. 프로파일링도 상시가 아니라 Day 4·Day 6 두 체크포인트로 한정한다.

Day 1에 아래 인터페이스 시그니처와 매니저 목록을 팀 전체가 확인하고 고정한다. 이후 임의 변경 시 별도 이슈를 먼저 발의한다 ([EXECUTION_PLAN.md](EXECUTION_PLAN.md) 에이전트 작업 규칙 참고).

## 0. 씬 구성

씬은 **2개**다. 빌드 순서도 이 순서다.

| # | 씬 | 역할 | 소유자 |
|---|---|---|---|
| 0 | `MainMenu` | 타이틀, 업그레이드 구매, 단계 표시, 시작 버튼 | 5. UI·연출 |
| 1 | `Game` | 인게임. 결과 화면은 이 씬 안의 UI 패널로 처리한다 | 2. 코어 플레이 |

**나눈 이유는 병합 충돌이다.** 씬 파일은 여러 명이 동시에 고치면 병합이 가장 어려운 파일이다. 메뉴를 만드는 사람(5번)과 인게임을 만드는 사람(2번)이 서로 다른 씬을 가지면 애초에 부딪힐 일이 없다.

**결과 화면을 별도 씬으로 만들지 않는 이유**는 반대다. 결과 화면은 이번 런의 획득 코인·청구서 내역을 그대로 받아 써야 하는데, 씬을 넘기면 그 데이터를 전달할 통로를 따로 만들어야 한다. 같은 씬의 패널이면 그냥 읽으면 된다.

### 매니저의 생존 — 부트스트랩 씬을 두지 않는 이유

매니저 초기화 전용 씬(`Bootstrap`)을 따로 두는 방식도 있지만 **쓰지 않는다.**

이유는 개발 중에 반드시 부딪히는 문제 때문이다. 개발자는 하루에도 수십 번 `Game` 씬만 열어놓고 Play 를 누른다. 부트스트랩 씬 방식이면 그때마다 매니저가 하나도 없어서 `NullReferenceException` 이 쏟아지고, 결국 "어느 씬에서 Play 해도 매니저가 생기게 하는 장치"를 또 만들게 된다. **그 장치를 만들면 부트스트랩 씬은 할 일이 없어진다.** 그래서 처음부터 장치 쪽만 남긴다.

매니저 프리팹을 `RuntimeInitializeOnLoadMethod` 로 씬 로드 전에 자동 생성하고 `DontDestroyOnLoad` 로 유지한다. MainMenu ↔ Game 을 오갈 때 다시 만들지 않는다. 구현은 1번(PM·통합) 담당이 한다 (작업 1.2.3).

이렇게 하면 관리할 씬이 하나 줄고, 아무 씬이나 열어서 바로 Play 할 수 있다.

### 씬 전환 시 정적 이벤트

씬을 오가므로 3절의 구독 해제 규칙이 **반드시** 지켜져야 한다. `GameEvents` 는 정적이라 씬이 바뀌어도 구독이 남고, Game 씬을 두 번째로 들어가는 순간 코인이 두 배로 들어오는 식의 버그가 난다.

## 1. 매니저 구조

싱글톤 매니저 여러 개를 부트스트랩 씬에서 초기화하는 구조를 쓴다.

**근거**: Zenject·VContainer 같은 DI 프레임워크는 배우고 세팅하는 데만 하루 이상 들 수 있어 7일 일정에 맞지 않는다. 반면 싱글톤+이벤트 버스는 러닝커브가 거의 없고, "Game/Economy/UI 이벤트 분리, 스크립트 간 직접 참조 차단" 원칙을 그대로 구현할 수 있다.

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

### 초기화 순서

부트스트랩 씬에서 아래 순서로 초기화한다. 순서가 어긋나면 EconomyManager가 저장된 코인을 읽기 전에 0으로 초기화하는 식의 버그가 난다.

```
1. SaveManager       (저장 데이터 로드 — 다른 매니저의 초기값 출처)
2. EconomyManager    (코인·업그레이드 레벨 복원)
3. StaminaManager / FeverManager / BillManager / PiggyManager
4. UIManager / AudioManager  (위 상태를 구독해 화면에 반영)
5. GameManager       (마지막에 MainMenu 상태로 진입)
```

### 담당 매핑

| 매니저 | 담당 |
|---|---|
| GameManager, PiggyManager, StaminaManager | 2. 코어 플레이 |
| EconomyManager, BillManager, SaveManager | 3. 성장·저장 |
| FeverManager | 4. 피버·보너스 |
| UIManager, AudioManager | 5. UI·연출 |

**근거**: [EXECUTION_PLAN.md](EXECUTION_PLAN.md)의 역할 분담표와 1:1로 맞춰, 담당자가 자기 매니저 외 코드를 건드릴 일을 최소화한다.

## 2. 공용 인터페이스

Day 1에 아래 시그니처를 고정한다.

```csharp
public enum HitSource { Hover, AutoHammer }

public readonly struct HitInfo {
    public readonly HitSource Source;   // 스태미나 소모 여부 판정에 사용
    public readonly long RawCoin;       // 배율·페널티 적용 전 원시 획득량
    public readonly Vector2 WorldPos;   // 숫자 팝업·파티클 위치
}

public interface IHittable {
    void OnHit(HitInfo info);      // 호버 자동 스윙 및 자동 망치가 호출
    bool IsAlive { get; }
}

public interface IBillService {
    int CurrentDay { get; }        // 런 1회 = 하루
    int DaysLeft { get; }          // 현재 청구서 마감까지 남은 일수. 0 에서 미납이면 파산
    float LoanDailyCut { get; }    // 대출 상환 전까지 매일 징수되는 수입 비율 (0 이면 대출 없음)
    bool TryPay(Bill bill);        // 조기 납부 포함
    bool TryTakeLoan(long amount); // 두 번째 청구서부터, 동시 1건
}

public interface IEconomyService {
    // amount 는 반드시 '가공 전 원시값'. 피버 배율과 대출 징수는
    // EconomyManager 내부에서만 적용한다 (아래 계약 참고).
    void AddCoin(long amount);
    bool TrySpendCoin(long amount);
    long CurrentCoin { get; }      // 누적 보유 코인
    long RunCoin { get; }          // 이번 런에서 획득한 코인 (단계 목표 판정 기준)
}

public interface ISaveService {
    SaveData Load();
    void Save(SaveData data);
}

[Serializable]
public class Bill {
    public long Amount;    // 청구 금액
    public int IssuedDay;  // 도착한 날짜
    public int DueDay;     // 이 날이 끝날 때까지 미납이면 파산
    public bool IsPaid;
}

[Serializable]
public class Loan {
    public long Principal;   // 빌린 금액
    public long Owed;        // 상환해야 할 총액 (원금 + 이자)
    public float DailyCut;   // 매일 징수되는 수입 비율. 이 징수분은 Owed 를 줄이지 않는다
    public int RepaidDay;    // 완제한 날짜. 이후 쿨다운 동안 재대출 불가
}

[Serializable]
public class SaveData {
    public int Version = 1;       // 마이그레이션 판단용, 필드 추가 시 증가
    public long TotalCoin;
    public int StageIndex;
    public long BestRunCoin;
    public int[] UpgradeLevels;   // 인덱스 = 업그레이드 3종 순서 고정
}
```

### 코인 배율 적용 계약

피버 배율과 대출 징수를 **호출측과 EconomyManager 양쪽에서 적용하는 이중 적용**은 2번(코어)과 3번(성장·저장) 경계에서 가장 나기 쉬운 버그다. 계약을 한쪽으로 고정한다.

- 호출측(PiggyManager, 자동 망치)은 `HitInfo.RawCoin`에 **가공 전 원시값만** 담아 넘긴다.
- 피버 배율, 보너스 배율, 대출 징수는 **EconomyManager 내부에서만** 순서대로 적용한다.
- 최종 지급액은 `OnCoinEarned` 이벤트로 방송하며, UI는 이 값만 표시한다 — UI가 배율을 다시 곱하지 않는다.

```text
지급액 = RawCoin × 피버 배율 × 보너스 배율 × (1 - 대출 일일 징수율)
```

`OnBankrupt`와 `OnStaminaDepleted`는 **둘 다 GameManager만 구독해 Result로 전이시킨다.** 다른 매니저가 각자 구독해 정리 로직을 돌리면 종료 처리 순서가 매번 달라져 "파산했는데 코인이 100% 정산되는" 류의 버그가 난다. 종료 절차는 GameManager가 단독으로 순서를 정해 호출한다.

**근거**: 타격 판정(`IHittable`)과 재화 처리(`IEconomyService`)를 인터페이스로 분리해두면, 2번(코어)과 3번(성장·저장) 담당자가 서로의 구현 세부사항 없이도 각자 프리팹·스크립트를 독립적으로 작업할 수 있다. `ISaveService`는 5번(UI)의 결과 화면이 저장 로직 내부 구현을 몰라도 되게 한다.

## 3. 이벤트 버스

정적 클래스 하나(`GameEvents`)에 C# `event Action<T>`를 모아 사용한다.

```csharp
public static class GameEvents {
    public static event Action<long> OnCoinEarned;
    public static event Action<Bill> OnBillIssued;
    public static event Action<Bill> OnBillPaid;
    public static event Action<int> OnDayEnded;      // 인자 = 종료된 날짜
    public static event Action<int> OnBillDueSoon;   // 인자 = 남은 일수 (HUD 경고용)
    public static event Action OnBankrupt;           // 마감일 미납 — 회차 종료
    public static event Action OnStaminaDepleted;
    public static event Action OnFeverStart;
    public static event Action OnFeverEnd;
}
```

**근거**: 메시징 프레임워크(예: UniRx, MessagePipe)를 새로 들이는 대신 C# 기본 이벤트만으로 "직접 참조 차단" 요구를 만족시킬 수 있다. 새 패키지 의존성을 늘리지 않는 편이 7일 일정에 안전하다.

### 구독 해제 규칙 (필수)

정적 이벤트는 씬을 다시 로드해도 구독이 남는다. 5명이 각자 구독하는 구조에서 이를 방치하면 **코인이 2배로 지급되거나, 파괴된 오브젝트를 참조해 `MissingReferenceException`이 뜨는** 증상으로 나타난다. 원인 추적이 오래 걸리는 부류의 버그이므로 규칙으로 강제한다.

- 구독은 `OnEnable`, 해제는 `OnDisable`에 **쌍으로** 작성한다. `Start`에서 구독하고 해제를 생략하지 않는다.
- `GameEvents`에 모든 이벤트를 `null`로 되돌리는 `ResetAll()`을 두고, GameManager가 런 시작 직전에 호출한다.
- Editor의 **Enter Play Mode Options(도메인 리로드 비활성화)를 켜지 않는다.** 켜면 정적 상태가 플레이 세션 사이에 그대로 남아 같은 증상이 재현된다.

## 4. 타격 대상 AI — FSM 채택

BT(비헤이비어 트리)가 아니라 **FSM**을 쓴다.

```
Idle → Moving → (호버 감지) BeingHit → (조건 충족) Fleeing → Moving (반복)
```

**근거**: BT는 여러 목표 사이의 우선순위 판단(공격/도망/순찰 등 다중 분기)이 필요할 때 이점이 있는데, 타격 대상의 행동은 "이동 → 피격 → 경직 → 재이동"이라는 선형적 상태 전이뿐이다. BT 도입은 노드 설계·디버깅 비용만 늘리고 실익이 없다. Enum 기반 커스텀 FSM 클래스(`enum TargetState { Idle, Moving, BeingHit, Fleeing }` + `switch` 전이) 하나로 충분하며, 로직과 애니메이션 상태를 분리해두면 디자이너가 애니메이션만 건드릴 때 코드 충돌이 줄어든다.

## 5. 프로파일링 계획 — 상시 아님, 체크포인트 2회

**근거**: 7일짜리 2D 단일 씬 게임에서 지속적 프로파일링은 시간 낭비다. 이미 자동 망치를 "물리 추적 대신 글로벌 타이머 적중"으로 설계해 사전 최적화가 되어 있으므로, 남은 리스크는 UI/파티클 쪽 흔한 실수 정도다. 체크포인트 방식으로 충분하다.

| 시점 | 방법 | 목적 |
|---|---|---|
| Day 4 (1차 통합 빌드 직후) | Unity Profiler CPU/Rendering 모듈 | Canvas 리빌드 과다, 드로우콜·그림자 캐스터 과다, `Update()` 남용 등 흔한 실수 조기 발견 |
| Day 6 (배포 후보 빌드) | Development Build + Profiler, Frame Debugger, Memory 모듈 | 드로우콜 수, GC 스파이크(파티클·숫자 팝업 인스턴스화) 정식 점검 |

**성능 예산** (Day 1에 팀 합의):
- 1920×1080 기준 60fps 유지
- 동시 파티클/피격 이펙트 인스턴스 상한 20개
- 드로우콜 200 이하, 화면 내 삼각형 15만 이하
- 그림자를 드리우는 오브젝트는 타격 대상만 — 파편과 코인은 그림자를 끈다
- 위 수치를 초과하면 "느낌상 괜찮다"가 아니라 이 기준으로 판단한다.
