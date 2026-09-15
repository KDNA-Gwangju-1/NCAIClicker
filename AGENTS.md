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

### 씬과 프리팹

- 씬은 `MainMenu`(0) / `Game`(1) 둘뿐이며 소유자가 정해져 있다. **자기 씬이 아니면 열어서 보되 저장하지 않는다.**
- 남의 씬에 뭔가 넣어야 하면 씬을 고치지 말고 **프리팹으로 만들어 넘긴다.**
- 타격 대상 프리팹은 로직을 루트에, 메시를 `Visual` 자식에 둔다. 그레이박스를 나중에 3D 에셋으로 갈아끼우기 위한 구조다.

### 작업 흐름

- 작업 번호(`2.3`, `4.1` 등)는 GitHub 이슈와 1:1로 대응한다. **완료 기준의 정본은 이슈 본문**이다.
- 착수 전 이슈의 담당자·상태를 확인하고, 비어 있으면 자신을 배정한 뒤 즉시 In Progress로 바꾼다.
- 한 작업 = 한 브랜치 = 한 PR. 브랜치명은 `feature/<모듈>-<이슈번호>-설명`.
- 공용 계약(인터페이스·이벤트·CSV 스키마)을 바꿔야 하면 구현 전에 이슈를 먼저 발의한다.
- 커밋 3회 또는 30분마다, 그리고 PR 직전에 `git fetch origin main && git rebase origin/main`.

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
