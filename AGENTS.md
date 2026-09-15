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
| 3D 에셋 | [docs/ASSET_PIPELINE.md](docs/ASSET_PIPELINE.md) |
| 빌드·배포 | [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md) |
| 이 기능이 **실제로 어떻게 구현됐나** | [docs/TECH_NOTES/](docs/TECH_NOTES/) — 이슈별 기술 문서 + C4 도식 |
| 브랜치·커밋·PR·이슈 | [docs/GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md) — **규칙 정본** |

## 지켜야 할 규칙

### 데이터

- **밸런스 수치의 원본은 `Assets/GameData/Balance/*.csv` 다.** 코드에 상수로 박지 않는다.
- `Assets/GameData/Generated/BalanceData.asset` 은 CSV에서 자동 생성되는 **산출물**이다. 손으로 고치지 않는다. 병합 충돌이 나면 해결하지 말고 재임포트한다.
- CSV를 고쳤으면 Unity 메뉴 `NCAI > 밸런스 CSV 임포트` (Ctrl+Shift+I)를 실행한다.
- **같은 숫자를 문서와 CSV 양쪽에 적지 않는다.** 문서는 규칙과 근거를, CSV는 값을 담는다.

### 코드

- 매니저는 싱글톤, 통신은 `GameEvents` 정적 이벤트. 스크립트 간 직접 참조 금지.
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
  Models/       3D 메시·FBX (이 프로젝트는 스프라이트를 쓰지 않는다)
  Materials/    머티리얼·텍스처
  Audio/        효과음·배경음
  GameData/     밸런스 CSV 원본과 거기서 생성된 에셋
  Settings/     URP 렌더러 설정
  ThirdParty/   외부에서 받은 에셋 원본
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
- **파일명은 클래스명과 같게 한다.** MonoBehaviour는 이게 어긋나면 컴포넌트가 붙지 않는다.
- 인터페이스는 `I` 접두어 (`IDamageable`). 열거형은 **파스칼 단수** (`FeverState`),
  `[Flags]` 를 붙일 때만 복수 (`AttackModes`).
- **메서드는 동사로 시작한다** (`GetDirection`, `FindTarget`). `bool` 을 반환하면 질문형으로
  (`IsGameOver`, `HasStartedTurn`), `bool` 필드도 동사 접두어를 쓴다 (`isDead`, `_isFeverActive`).
- **public 필드를 만들지 않는다.** 밖에서 읽어야 하면 getter 프로퍼티로 연다
  (`public int Score { get; private set; }`). 인스펙터 노출은 `[SerializeField] private` 로 한다.
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
  `feature/<이니셜>-<모듈>-<이슈번호>-<설명>` (예: `feature/KSH-economy-12-upgrade-cost`)
- 한 작업 = 한 브랜치 = 한 PR. 브랜치 수명은 하루를 넘기지 않는다.
- 커밋 3회 또는 30분마다, PR 직전에 `git fetch origin Develop && git rebase origin/Develop`.
- 커밋 메시지는 `feat:` `fix:` `refactor:` `docs:` `chore:` `ci:` + 한국어 요약.
  **한 커밋에 한 가지 일만** 담고 `git add -A` 를 쓰지 않는다.
- **이슈가 작업 지시서다.** 완료 기준의 정본은 이슈 본문이다. 착수 전 담당자·상태를 확인하고
  비어 있으면 자신을 배정한 뒤 즉시 In Progress로 바꾼다.
- 공용 계약(인터페이스·이벤트·CSV 스키마)을 바꿔야 하면 **구현 전에** 이슈를 먼저 발의한다
  (`공용 계약 변경` 이슈 템플릿).
- 머지는 사람이 한다. 리뷰어 최소 1명 승인, Squash and merge.

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

이 저장소를 처음 보는 에이전트에게는 아래 형태로 지시한다. **문서를 통째로 읽히지 말 것** — 필요한 것만 짚어준다.

```
이 저장소의 AGENTS.md 를 먼저 읽어라.
그다음 GitHub 이슈 #<번호> 를 확인하고, 그 이슈가 참조하는 문서 절만 읽어라.
작업은 해당 이슈 범위 안에서만 한다. 공용 계약(인터페이스·이벤트·CSV 스키마)을
바꿔야 하면 구현하지 말고 이슈에 코멘트로 보고해라.
끝나면 Unity Edit Mode 또는 Play Mode 검증 결과를 PR 본문에 적어라.
```

도구별로 읽는 파일이 다르므로 저장소에 포인터를 둔다. 새 도구를 쓰게 되면
그 도구가 찾는 파일명으로 포인터를 하나 더 만들고, **내용은 이 파일에만 적는다.**

| 도구 | 읽는 파일 |
|---|---|
| Claude Code | `CLAUDE.md` → 이 파일을 가리킴 |
| Codex / GPT, Antigravity, Cursor, Aider 등 | `AGENTS.md` (이 파일) |

모듈별로 읽혀야 할 것:

| 담당 | 이슈 | 필수 문서 |
|---|---|---|
| 2. 코어 플레이 | 2.x | ARCHITECTURE 0·2·3절, PATTERNS 4·5절 |
| 3. 성장·저장 | 3.x, 4.x | ARCHITECTURE 2·3절, BALANCE 6절, PATTERNS 5·6절 |
| 4. 피버·보너스 | 5.x | ARCHITECTURE 3절, BALANCE 6절 |
| 5. UI·연출 | 6.x | ARCHITECTURE 3절, PATTERNS 3·7절, ASSET_PIPELINE |
| 1. PM·통합 | 1.x, 7.x, 8.x | 전부 |

**에이전트가 하지 말아야 할 것**: 남의 씬·프리팹 직접 수정, 공용 계약 임의 변경, 생성된 `BalanceData.asset` 직접 편집, 진행률 추측.

### 작업 순서 (필수)

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

| 경로 | 무엇 |
|---|---|
| `.claude/skills/commit/` | `/commit` — 커밋 규칙에 맞춰 커밋 |
| `.claude/skills/pr/` | `/pr` — 리베이스·규칙 점검 후 PR 생성 |
| `.claude/skills/new-script/` | `/new-script` — 계획 승인 → 작성 → 점검 |
| `.claude/skills/tech-note/` | `/tech-note` — 기술 문서 + Mermaid C4 도식 작성·갱신 |
| `.claude/skills/verify-docs/` | `/verify-docs` — 기술 문서와 실제 코드를 대조 (읽기 전용) |
| `.claude/agents/convention-checker.md` | 공용 계약 위반 점검 (읽기 전용) |

**규칙 원문은 이 파일과 `docs/` 에만 있다.** 스킬과 에이전트는 규칙을 옮겨 적지 않고
"어느 문서를 읽으라"고만 적는다 — 양쪽에 적어 두면 한쪽만 고쳐졌을 때 어느 것이 맞는지 알 수 없다.

## 새 세션 또는 다른 PC에서 이어서 작업할 때

1. `git pull` — 문서·CSV·생성된 밸런스 에셋이 모두 저장소에 있다
2. Unity Hub에서 **6000.3.21f1** 로 연다 (`ProjectSettings/ProjectVersion.txt` 고정)
3. UnityYAMLMerge 등록 — [RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md) 2절. **PC마다 1회 필요하다**
4. 현재 진행 상황은 [칸반 보드](https://github.com/orgs/KDNA-Gwangju-1/projects/2/views/2)에서 확인한다
5. 에이전트가 Unity를 직접 조작하려면 MCP 서버가 필요하다. Unity 패키지는 이미 `Packages/manifest.json` 에 있으므로 별도 설치가 필요 없지만, **Python 3.10+ 와 `uv` 는 PC마다 설치**해야 한다

### 저장소에 없는 것 (PC마다 다시 필요)

| 항목 | 비고 |
|---|---|
| `.claude/settings.local.json` | 개인 설정. gitignore됨 |
| `research/transcripts/` | 원작 플레이 영상 자막. 타인의 저작물이라 커밋하지 않는다. 조사 결과는 REFERENCE_ANALYSIS 에 정리되어 있으므로 다시 받을 필요는 없다 |
| UnityYAMLMerge git 설정 | 위 3번 |
| `Library/` | Unity가 재생성한다. 첫 실행이 느린 것은 정상 |

## 프로젝트 고정 사항

바꾸려면 팀 논의가 필요한 것들이다.

- Unity **6000.3.21f1**, URP 기본 렌더러 (2D Renderer 쓰지 않음)
- **3D** 저폴리 오브젝트 + 고정 탑다운 카메라. 스프라이트·빌보드 쓰지 않음
- 16:9 고정, PC Windows 스탠드얼론
- Product Name `NCAIClicker` / Company Name `NCAITeamTwo` — **저장 경로에 들어가므로 변경 금지**
- 3D 에셋은 VARCO 3D 로 생성
