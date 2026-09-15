# NCAIClicker

NCAI 과정 팀 프로젝트(자유 주제) — 5인 7일간 Unity로 제작하는 스태미나 기반 인크리멘탈 게임.

## 처음 오셨다면

**순서대로 세 개만 읽으면 작업을 시작할 수 있습니다.**

1. [게임 기획서 (GDD)](docs/GDD.md) — 우리가 뭘 만드는지
2. [실행 계획](docs/EXECUTION_PLAN.md) — 내 담당과 작업 순서
3. [아키텍처](docs/ARCHITECTURE.md) — 코드를 어떤 계약으로 짜는지

그다음 [칸반 보드](https://github.com/orgs/KDNA-Gwangju-1/projects/2/views/2)에서 자기 번호의 카드를 집으면 됩니다. 카드 본문에 담당·선행·완료 기준이 있습니다.

작업 규칙 요약과 PC 세팅은 [AGENTS.md](AGENTS.md)에 있습니다. **AI 에이전트에게 작업을 맡길 때도 이 파일을 먼저 읽히세요** — Claude Code, Codex, Antigravity 등 도구를 가리지 않는 공통 진입점입니다.

## 문서

| 문서 | 답하는 질문 |
|---|---|
| [GDD](docs/GDD.md) | 게임이 **무엇인가** — 규칙 |
| [실행 계획](docs/EXECUTION_PLAN.md) | **누가 무엇을 언제** — 작업 순서와 의존 |
| [아키텍처](docs/ARCHITECTURE.md) | 코드를 **어떤 계약으로** 짜나 |
| [설계 패턴](docs/PATTERNS.md) | **왜 그 설계**인가 — 학습용 |
| [밸런스 설계표](docs/BALANCE.md) | 숫자가 **왜 그 값**인가 (원본은 `Assets/GameData/Balance/*.csv`) |
| [원작 분석](docs/REFERENCE_ANALYSIS.md) | 원작은 **실제로 어떤가**, 무엇을 따르고 무엇을 뺐나 |
| [3D 에셋 파이프라인](docs/ASSET_PIPELINE.md) | 에셋을 **어떻게 만들고 끼우나** |
| [배포 체크리스트](docs/RELEASE_CHECKLIST.md) | 빌드 전에 **뭘 확인하나** |
| [서드파티 라이선스](docs/THIRD_PARTY.md) | 외부 에셋 **출처와 라이선스** |

## 진행 현황

[![진행 현황](https://kdna-gwangju-1.github.io/NCAIClicker/badge.svg)](https://kdna-gwangju-1.github.io/NCAIClicker/)

이미지를 클릭하면 카드별 상세 보드로 이동합니다. **이슈 변경 시와 main 에 push 할 때** 자동 갱신됩니다. (30분 예약 실행도 걸려 있으나 GitHub 이 자주 건너뛰므로 신뢰하지 않는다. 즉시 갱신이 필요하면 Actions 탭에서 Run workflow 를 누른다.)

- [칸반 보드 원본](https://github.com/orgs/KDNA-Gwangju-1/projects/2/views/2)

카드 번호는 [실행 계획](docs/EXECUTION_PLAN.md)의 작업 번호와 1:1로 대응한다. 날짜가 아니라 **선후 관계**로 묶여 있으므로, 선행 작업이 끝난 카드는 담당이 다르면 동시에 진행한다.
