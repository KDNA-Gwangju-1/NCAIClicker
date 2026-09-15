# 아키텍처 및 프로파일링 계획

5인 7일 규모에서는 DI 프레임워크나 BT(비헤이비어 트리) 같은 범용 솔루션이 오히려 구현·디버깅 비용을 늘린다. 싱글톤 매니저 + 정적 이벤트 버스, 타격 대상은 FSM으로 충분하다. 프로파일링도 상시가 아니라 Day 4·Day 6 두 체크포인트로 한정한다.

Day 1에 아래 인터페이스 시그니처와 매니저 목록을 팀 전체가 확인하고 고정한다. 이후 임의 변경 시 별도 이슈를 먼저 발의한다 ([EXECUTION_PLAN.md](EXECUTION_PLAN.md) 에이전트 작업 규칙 참고).

## 1. 매니저 구조

싱글톤 매니저 여러 개를 부트스트랩 씬에서 초기화하는 구조를 쓴다.

**근거**: Zenject·VContainer 같은 DI 프레임워크는 배우고 세팅하는 데만 하루 이상 들 수 있어 7일 일정에 맞지 않는다. 반면 싱글톤+이벤트 버스는 러닝커브가 거의 없고, "Game/Economy/UI 이벤트 분리, 스크립트 간 직접 참조 차단" 원칙을 그대로 구현할 수 있다.

```
GameManager      런 상태 머신 (MainMenu → Running → Result)
StaminaManager   스태미나 소모/회복
BillManager      청구서 스케줄링, 추심원 페널티
PiggyManager     타격 대상 스폰, 개별 FSM 구동
EconomyManager   코인, 업그레이드 비용/레벨 계산
FeverManager     피버 게이지, 배율
SaveManager      SaveData DTO 직렬화
UIManager        HUD/화면 전환
AudioManager     SFX/BGM
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
public interface IHittable {
    void OnHit(HitInfo info);      // 호버 자동 스윙 및 자동 망치가 호출
    bool IsAlive { get; }
}

public interface IEconomyService {
    void AddCoin(long amount);
    bool TrySpendCoin(long amount);
    long CurrentCoin { get; }
}

public interface ISaveService {
    SaveData Load();
    void Save(SaveData data);
}
```

**근거**: 타격 판정(`IHittable`)과 재화 처리(`IEconomyService`)를 인터페이스로 분리해두면, 2번(코어)과 3번(성장·저장) 담당자가 서로의 구현 세부사항 없이도 각자 프리팹·스크립트를 독립적으로 작업할 수 있다. `ISaveService`는 5번(UI)의 결과 화면이 저장 로직 내부 구현을 몰라도 되게 한다.

## 3. 이벤트 버스

정적 클래스 하나(`GameEvents`)에 C# `event Action<T>`를 모아 사용한다.

```csharp
public static class GameEvents {
    public static event Action<long> OnCoinEarned;
    public static event Action<Bill> OnBillIssued;
    public static event Action<Bill> OnBillPaid;
    public static event Action OnStaminaDepleted;
    public static event Action OnFeverStart;
    public static event Action OnFeverEnd;
}
```

**근거**: 메시징 프레임워크(예: UniRx, MessagePipe)를 새로 들이는 대신 C# 기본 이벤트만으로 "직접 참조 차단" 요구를 만족시킬 수 있다. 새 패키지 의존성을 늘리지 않는 편이 7일 일정에 안전하다.

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
| Day 4 (1차 통합 빌드 직후) | Unity Profiler CPU/Rendering 모듈 | Canvas 리빌드 과다, 파티클 오버드로우, `Update()` 남용 등 흔한 실수 조기 발견 |
| Day 6 (배포 후보 빌드) | Development Build + Profiler, Frame Debugger, Memory 모듈 | 드로우콜 수, GC 스파이크(파티클·숫자 팝업 인스턴스화) 정식 점검 |

**성능 예산** (Day 1에 팀 합의):
- 1920×1080 기준 60fps 유지
- 동시 파티클/피격 이펙트 인스턴스 상한 20개
- 위 수치를 초과하면 "느낌상 괜찮다"가 아니라 이 기준으로 판단한다.
