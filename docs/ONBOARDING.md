# 이어서 작업하기 — 온보딩과 환경

> `AGENTS.md` 에서 **매 세션 읽을 필요는 없지만 사람이 한 번은 읽어야 하는 절**을 옮겨 둔 곳이다.
> 지켜야 할 규칙의 원문은 여전히 [AGENTS.md](../AGENTS.md) 와 `docs/` 에만 있다.
> 여기에는 규칙을 새로 적지 않는다.

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


### 저장소에 들어 있는 Claude Code 설정

| 경로 | 무엇 |
|---|---|
| `.claude/skills/commit/` | `/commit` — 커밋 규칙에 맞춰 커밋 |
| `.claude/skills/pr/` | `/pr` — 리베이스·규칙 점검 후 PR 생성 |
| `.claude/skills/start-work/` | `/start-work` — 배정 → 칸반 In Progress → Develop에서 브랜치 |
| `.claude/skills/list-work/` | `/list-work` — 착수 가능한 이슈 목록 (읽기 전용) |
| `.claude/skills/new-script/` | `/new-script` — 계획 승인 → 작성 → 점검 |
| `.claude/skills/tech-note/` | `/tech-note` — 기술 문서 + Mermaid C4 도식 작성·갱신 |
| `.claude/skills/verify-docs/` | `/verify-docs` — 기술 문서와 실제 코드를 대조 (읽기 전용) |
| `.claude/agents/convention-checker.md` | 공용 계약 위반 점검 (읽기 전용) |


## 새 세션 또는 다른 PC에서 이어서 작업할 때

1. `git pull` — 문서·CSV·생성된 밸런스 에셋이 모두 저장소에 있다
2. Unity Hub에서 **6000.3.21f1** 로 연다 (`ProjectSettings/ProjectVersion.txt` 고정)
3. UnityYAMLMerge 등록 — [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) 2절. **PC마다 1회 필요하다**
4. 현재 진행 상황은 [칸반 보드](https://github.com/orgs/KDNA-Gwangju-1/projects/2/views/2)에서 확인한다
5. 에이전트가 Unity를 직접 조작하려면 MCP 서버가 필요하다. Unity 패키지는 이미 `Packages/manifest.json` 에 있으므로 별도 설치가 필요 없지만, **Python 3.10+ 와 `uv` 는 PC마다 설치**해야 한다

### 저장소에 없는 것 (PC마다 다시 필요)

| 항목 | 비고 |
|---|---|
| `.claude/settings.local.json` | 개인 설정. gitignore됨 |
| `research/transcripts/` | 원작 플레이 영상 자막. 타인의 저작물이라 커밋하지 않는다. 조사 결과는 REFERENCE_ANALYSIS 에 정리되어 있으므로 다시 받을 필요는 없다 |
| `research/reference/press-kit/` | 원작 아트 레퍼런스(스크린샷·키아트). **`python tools/fetch_reference.py` 로 다시 받는다.** 출처는 공식 프레스킷 <https://rikegames.com/press/bills-must-be-paid/> 이며, 커밋하지 않는 이유와 사용 경계는 [ASSET_PIPELINE.md](ASSET_PIPELINE.md) 1-1 절에 있다 |
| UnityYAMLMerge git 설정 | 위 3번 |
| `Library/` | Unity가 재생성한다. 첫 실행이 느린 것은 정상 |

## 프로젝트 고정 사항

바꾸려면 팀 논의가 필요한 것들이다.

- Unity **6000.3.21f1**, URP 기본 렌더러 (2D Renderer 쓰지 않음)
- **3D** 저폴리 오브젝트 + 고정 탑다운 카메라. 스프라이트·빌보드 쓰지 않음
- 16:9 고정, PC Windows 스탠드얼론
- Product Name `NCAIClicker` / Company Name `NCAITeamTwo` — **저장 경로에 들어가므로 변경 금지**
- 3D 에셋은 VARCO 3D 로 생성
