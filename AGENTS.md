# NCAIClicker — 작업 지침

> **모든 AI 코딩 에이전트의 진입점이다.** 도구를 가리지 않는다 — Claude Code, Codex/GPT,
> Antigravity, Cursor, Aider 무엇을 쓰든 이 파일을 먼저 읽는다.
> `CLAUDE.md` 는 이 파일을 가리키는 포인터일 뿐이니 내용을 양쪽에 적지 않는다.

NCAI 과정 팀 프로젝트. 5명이 7일간 Unity로 만드는 **시간 제한형 액티브 인크리멘탈 게임**이다.
[Bills Must Be Paid](https://store.steampowered.com/app/4421010/_Bills_Must_Be_Paid/)(Rike Games) 모작으로 시작한다.

## 먼저 읽을 것

작업 종류에 따라 필요한 문서만 읽는다. 전부 읽을 필요는 없다.

| 하려는 일 | 읽을 문서 |
|---|---|
| 게임 규칙 파악 | [docs/GDD.md](docs/GDD.md) |
| 내가 뭘 해야 하나 | [docs/EXECUTION_PLAN.md](docs/EXECUTION_PLAN.md) + 배정된 GitHub 이슈 |
| 코드 작성 | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 인터페이스·이벤트·매니저 계약 |
| 설계 판단이 필요할 때 | [docs/PATTERNS.md](docs/PATTERNS.md) — 쓰는 패턴과 쓰지 않는 패턴 |
| 수치를 바꿔야 할 때 | [docs/BALANCE.md](docs/BALANCE.md) |
| "원작은 어떻게 했나" | [docs/REFERENCE_ANALYSIS.md](docs/REFERENCE_ANALYSIS.md) |
| 3D 에셋 · 원작 아트 레퍼런스 | [docs/ASSET_PIPELINE.md](docs/ASSET_PIPELINE.md) — 레퍼런스는 `python tools/fetch_reference.py` 로 받는다 |
| 3D 만들 이미지·프롬프트 | [docs/CONCEPT_ART/README.md](docs/CONCEPT_ART/README.md) — 입력 이미지·VARCO URL·Generate3D 설정. 프롬프트는 [PROMPTS.md](docs/CONCEPT_ART/PROMPTS.md) 번호로 참조 |
| UI 제작·수정 | [docs/UI_GUIDE.md](docs/UI_GUIDE.md) — 4배수 그리드·대비·세이프존·글자 크기 |
| 빌드·배포 | [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md) |
| 이 기능이 **실제로 어떻게 구현됐나** | [docs/TECH_NOTES/](docs/TECH_NOTES/) — 이슈별 기술 문서 + C4 도식 |
| 브랜치·커밋·PR·이슈 | [docs/GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md) — **규칙 정본** |
| 새 PC·새 세션 시작, 에이전트에게 위임 | [docs/ONBOARDING.md](docs/ONBOARDING.md) — 환경 준비·도구별 진입점·고정 사항 |

## 지켜야 할 규칙

### 데이터

- **밸런스 수치의 원본은 `Assets/GameData/Balance/*.csv` 다.** 코드에 상수로 박지 않는다.
- `Assets/GameData/Generated/BalanceData.asset` 은 CSV에서 자동 생성되는 **산출물**이다. 손으로 고치지 않는다. 병합 충돌이 나면 해결하지 말고 재임포트한다.
- CSV를 고쳤으면 Unity 메뉴 `NCAI > 밸런스 CSV 임포트` (Ctrl+Shift+I)를 실행한다.
- **같은 숫자를 문서와 CSV 양쪽에 적지 않는다.** 문서는 규칙과 근거를, CSV는 값을 담는다.

### 코드

- 매니저는 싱글톤을 출발점으로 삼고, 상태 변화는 `GameEvents` 정적 이벤트로 알린다. 다른 매니저 구현 클래스를 직접 참조하지 않는다. 요청·조회는 ARCHITECTURE의 공용 인터페이스를 사용하고, 초기화·종료 순서는 GameManager가 조정한다.
- 정적 이벤트는 `OnEnable` 구독 / `OnDisable` 해제를 **쌍으로** 쓴다. 빠뜨리면 코인이 두 배로 들어오는 버그가 난다.
- 코인 배율과 대출 징수는 **EconomyManager 안에서만** 적용한다. 호출측은 가공 전 원시값만 넘긴다.
- Editor의 Enter Play Mode Options(도메인 리로드 비활성화)를 켜지 않는다.

### 폴더 배치

에셋은 **종류별 최상위 폴더**로 나눈다. 이 목록이 전부다.

```
Assets/
  Scripts/      C# 스크립트
    Runtime/    게임에 빌드되는 코드
    Editor/     에디터 전용 (임포터·툴). 빌드에 포함되지 않는다
  Scenes/       씬 — MainMenu, Game 둘뿐이다
  Prefabs/      프리팹
    Props/      소품 프리팹 (#236 합의)
      Small/    소형 소품 (예: 랜턴·곡괭이)
      Large/    대형 소품 (예: 갱도 입구·목책)
  Models/       3D 메시·FBX (이 프로젝트는 스프라이트를 쓰지 않는다)
  Materials/    머티리얼·텍스처
  Audio/        효과음·배경음
  GameData/     밸런스 CSV 원본과 거기서 생성된 에셋
  Settings/     URP 렌더러 설정
  ThirdParty/   외부에서 받은 에셋 원본
  Tests/        Test Framework 테스트 (PlayMode·EditMode). 빌드에 포함되지 않는다
  TextMesh Pro/ TMP 필수 리소스 (Unity 가 import 시 생성). 손으로 고치지 않는다
```

- **맞는 폴더가 없으면 새로 만들기 전에 이슈에 묻는다.** 임의로 폴더를 늘리지 않는다.
- **`ThirdParty/` 안의 파일은 수정하지 않는다.** 고쳐야 하면 복사해서 우리 폴더에 두고 고친다 —
  원본을 고치면 나중에 갱신본을 받을 때 무엇을 바꿨는지 알 수 없다.
- `Scripts/Runtime/` 아래를 기능별로 더 나눌 때는 **파일이 실제로 쌓인 뒤에** 나눈다.
  비어 있는 폴더를 미리 만들어 두지 않는다.
- 파일을 옮기거나 이름을 바꿀 때는 탐색기가 아니라 **Unity 에디터의 Project 창에서** 한다.
  `.meta` 가 기억하는 참조가 끊어진다. `.meta` 는 원본 파일과 **같은 커밋에** 담는다.

### 명명

- C# 네이밍은 `.editorconfig` 가 정본이다 — private 필드 `_camelCase`, 클래스·메서드·프로퍼티
  `PascalCase`, 지역 변수·매개변수 `camelCase`, 상수 `PascalCase`.
  참고: [Unity 공식 네이밍·코드 스타일 가이드](https://unity.com/kr/how-to/naming-and-code-style-tips-c-scripting-unity)
- **파일명과 폴더명은 ASCII만 쓴다. 한글·공백·특수문자 금지** — 문서·스크립트·에셋 전부.
  팀에 macOS 사용자가 있어서 이건 취향 문제가 아니다: macOS는 한글 파일명을 자모 분리(NFD)로
  저장하고 Windows는 완성형(NFC)으로 저장해 **git이 같은 파일을 서로 다른 파일로 본다.**
  아무것도 안 고쳤는데 `git status` 에 뜨고, 같은 이름 파일이 둘로 늘고, diff가 빈 충돌이 난다.
  **읽는 사람이 보는 이름은 문서 제목(H1)과 링크 텍스트로 한글을 쓴다** — 파일명은 식별자일 뿐이다.
- **파일명은 클래스명과 같게 한다.** MonoBehaviour는 이게 어긋나면 컴포넌트가 붙지 않는다.
- 인터페이스는 `I` 접두어 (`IDamageable`). 열거형은 **파스칼 단수** (`FeverState`),
  `[Flags]` 를 붙일 때만 복수 (`AttackModes`).
- **메서드는 동사로 시작한다** (`GetDirection`, `FindTarget`). `bool` 을 반환하면 질문형으로
  (`IsGameOver`, `HasStartedTurn`), `bool` 필드도 동사 접두어를 쓴다 (`isDead`, `_isFeverActive`).
- **public 필드를 만들지 않는다.** 밖에서 읽어야 하면 getter 프로퍼티로 연다
  (`public int Score { get; private set; }`). 인스펙터 노출은 `[SerializeField] private` 로 한다.
  - **직렬화 데이터만 예외**: CSV 산출물 `BalanceData` 및 그 `[Serializable]` 데이터 클래스, 저장 DTO는 Unity 직렬화용 public 필드를 허용한다. 런타임 매니저·컴포넌트에는 적용하지 않는다. 생성 밸런스 데이터는 런타임에서 읽기만 하며 업그레이드 결과는 별도 상태에 계산한다.
- 이벤트는 상태 변화를 나타내는 동사구로 (`DoorOpened`), 발생시키는 메서드는 `On` 접두어로
  (`OnDoorOpened`). 델리게이트는 `System.Action` 을 쓴다.
- 에셋 파일도 `PascalCase` 로 짓는다 (`HammerBase.fbx`, `CoinPickup.wav`). 공백·한글·숫자 접두 금지.
- **생성형 AI(VARCO 3D 등)로 만든 에셋은 넣기 전에 그 도구의 상업 이용 조건을 확인하고**
  [THIRD_PARTY.md](docs/THIRD_PARTY.md) 표를 같은 PR에서 갱신한다.

### 씬과 프리팹

- 씬은 `MainMenu`(0) / `Game`(1) 둘뿐이며 소유자가 정해져 있다. **자기 씬이 아니면 열어서 보되 저장하지 않는다.**
- 남의 씬에 뭔가 넣어야 하면 씬을 고치지 말고 **프리팹으로 만들어 넘긴다.**
- 타격 대상 프리팹은 로직을 루트에, 메시를 `Visual` 자식에 둔다. 그레이박스를 나중에 3D 에셋으로 갈아끼우기 위한 구조다.

### 작업 흐름 (요약 — 정본은 [GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md))

- **브랜치는 `main` 이 아니라 `Develop` 에서 딴다. PR 대상도 `Develop` 이다.**
  `feature/<이니셜>-<모듈>-<이슈번호>-<계획번호>-<설명>` (예: `feature/KSH-economy-12-3_2_1-upgrade-cost`)
- 한 작업 = 한 브랜치 = 한 PR. 브랜치 수명은 하루를 넘기지 않는다.
- 커밋 3회 또는 30분마다, PR 직전에 `git fetch origin Develop && git rebase origin/Develop`.
- 커밋 메시지는 `feat:` `fix:` `refactor:` `docs:` `chore:` `ci:` + 한국어 요약.
  **한 커밋에 한 가지 일만** 담고 `git add -A` 를 쓰지 않는다.
- **이슈가 작업 지시서다.** 완료 기준의 정본은 이슈 본문이다. 착수 전 담당자·상태를 확인하고
  비어 있으면 자신을 배정한 뒤 즉시 In Progress로 바꾼다.
- **착수 가능 여부의 정본은 칸반 보드의 Status 다.** 담당자(assignee)는 판정에 쓰지 않는다 —
  배정과 Status 변경은 별개의 단계라 한쪽만 보면 어긋난다. Status 는 이슈 필드가 아니라
  Projects v2 아이템 필드여서 `gh issue list` 결과에 **나오지 않으므로**, 목록을 만들 때는
  반드시 `gh project item-list` 로 보드를 읽는다.
- **Status 와 담당자가 어긋난 이슈는 착수 가능·불가로 분류하지 말고 따로 보고한다.**
  `In Progress` 인데 담당자가 없거나 담당자가 있는데 `Todo` 인 것은 착수 절차가 중간에 끊긴
  흔적이다. 조용히 걸러 내면 목록에서 사라질 뿐 아무도 그 사실을 모른다.
- 공용 계약(인터페이스·이벤트·CSV 스키마)을 바꿔야 하면 **구현 전에** 이슈를 먼저 발의한다
  (`공용 계약 변경` 이슈 템플릿).
- **이슈를 만들거나 고칠 때 `**선행**:` 줄을 지우지 않는다.** 대시보드가 이 줄을 읽어
  "지금 착수 가능"을 자동 계산한다. 선행이 없으면 `**선행**: 없음` 이라고 적는다
  (형식은 [GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md) 3절).
- 머지는 사람이 한다. **리뷰는 올리는 사람이 직접 한다** (타인에게 요청하지 않는다). Squash and merge.

## Unity 에디터 연결 (MCP) — 조회와 변경의 경계

MCP가 붙어 있으면 에이전트가 에디터 상태를 직접 읽고 바꿀 수 있다. **읽는 것은 자유,
바꾸는 것은 승인을 받는다.**

| 구분 | 내용 |
|---|---|
| 승인 없이 해도 되는 것 | 콘솔 로그 읽기, 씬 하이어라키·컴포넌트 조회, 에셋 경로 조회, 컴파일 오류 확인 |
| 승인을 받고 하는 것 | 오브젝트 생성·삭제, 컴포넌트 부착, 인스펙터 값 변경, 프리팹 수정, 씬 저장 |
| 하지 않는 것 | **자기 담당이 아닌 씬 저장**, Play Mode 진입 후 방치, Project Settings 변경, 패키지 추가·제거 |

콘솔 오류를 다룰 때는 **원인과 수정안을 먼저 보고하고 승인을 받은 뒤 고친다.** 오류 메시지를
사람이 복사해 붙여 넣을 필요는 없다 — MCP로 직접 읽는다.

## 에이전트에게 작업을 맡길 때

지시 문구 예시, 도구별 진입 파일, 담당 모듈별로 읽어야 할 문서 표는
[docs/ONBOARDING.md](docs/ONBOARDING.md) 에 있다. **문서를 통째로 읽히지 말 것** — 필요한 절만 짚어준다.

**에이전트가 하지 말아야 할 것**: 남의 씬·프리팹 직접 수정, 공용 계약 임의 변경, 생성된 `BalanceData.asset` 직접 편집, 진행률 추측.

### 작업 순서 (필수)

0. **착수 절차. 코드를 읽기도 전에 이것부터 한다.** (`start-work` 스킬이 이 셋을 실행한다)
   1. 이슈를 확인한다. 담당자가 비어 있으면 자신을 배정한다.
   2. 칸반 보드의 Status 를 `In Progress` 로 바꾼다. **확인과 변경 사이가 벌어지면 두 사람이 같은 이슈를 집는다.**
   3. `Develop` 에서 `feature/<이니셜>-<모듈>-<이슈번호>-<계획번호>-<설명>` 브랜치를 만들고 체크아웃한다.
      구분자는 **하이픈**이고 계획번호는 점을 `_` 로 바꾼다 (예: `feature/KSH-economy-12-3_2_1-upgrade-cost`).
      슬래시로 나누지 않는다.
   4. 셋의 결과를 보고한 뒤 1번으로 간다. **셋 중 하나라도 못 했으면 멈추고 묻는다** —
      브랜치 없이 쓴 코드는 남의 브랜치를 오염시키고, 보드에 없는 작업은 팀에게 존재하지 않는다.
1. 기능 구현을 요청받으면 **바로 코드를 쓰지 않는다.** 만들거나 고칠 파일과 방법을 계획으로
   먼저 보여 주고, 승인을 받은 뒤 작성한다. (`new-script` 스킬이 이 순서를 고정해 둔다.)
2. 스크립트를 만들거나 고친 뒤에는 **`convention-checker` 에이전트로 공용 계약 위반을 점검**하고,
   결과를 보고에 함께 싣는다.
3. **검수를 통과했으면 이슈를 닫기 전에 기술 문서를 쓴다** — `docs/TECH_NOTES/` 규칙을 따르고
   코드와 같은 PR에 담는다. 나중에 몰아 쓰지 않는다. 그 기능의 문서가 이미 있으면 **갱신**한다.
4. **결과가 겉보기엔 멀쩡한데 뭔가 이상하면 `/verify-docs` 로 문서와 코드를 대조한다.**
   그 느낌은 대체로 맞다. 놓친 것이 누적되면 나중에는 AI에게 시켜도 못 고친다.
5. 커밋·push·PR은 **요청받았을 때만** 한다.
6. 요청이 모호하거나 문서에 없는 결정이 필요하면 추측하지 않고 묻는다.
7. 계획이 여러 번 어긋나 대화가 길어지면, 브리핑을 임시 문서로 정리한 뒤 `/clear` 하고
   "그 문서를 읽고 착수"로 새 세션에서 시작한다.

### 저장소에 들어 있는 Claude Code 설정

스킬·에이전트 목록은 [docs/ONBOARDING.md](docs/ONBOARDING.md) 에 있다.

**규칙 원문은 이 파일과 `docs/` 에만 있다.** 스킬과 에이전트는 규칙을 옮겨 적지 않고
"어느 문서를 읽으라"고만 적는다 — 양쪽에 적어 두면 한쪽만 고쳐졌을 때 어느 것이 맞는지 알 수 없다.

## 새 세션·새 PC·프로젝트 고정 사항

환경 준비 절차, 저장소에 없어서 PC마다 다시 필요한 것, 팀 논의 없이 바꾸지 않는 고정 사항
(Unity 버전, URP, 16:9, Product Name 등)은 [docs/ONBOARDING.md](docs/ONBOARDING.md) 에 있다.
