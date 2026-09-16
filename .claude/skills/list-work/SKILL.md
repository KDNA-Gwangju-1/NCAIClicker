---
name: list-work
description: 지금 착수할 수 있는 이슈가 무엇인지 물었을 때 사용합니다 ("남은 이슈 리스트업", "뭐 작업하면 돼?", "할 수 있는 거 뭐 있어?"). 칸반 보드의 Status를 정본으로 읽고 선행 이슈를 대조합니다. 읽기 전용이며 보드·이슈·브랜치를 건드리지 않습니다.
---

# 착수 가능한 이슈 목록

판정 규칙의 정본은 `AGENTS.md` 의 **"작업 흐름 (요약)"** 절, 선행 줄 형식의 정본은
`docs/GIT_WORKFLOW.md` **3절**이다. 이 파일은 실행 방법만 적는다.

## 실행

```bash
python .claude/skills/list-work/list_work.py
```

**직접 판정 코드를 짜지 않는다.** 매번 새로 짜면 규칙이 조금씩 달라지는데 출력은 그럴듯해
보여서 틀린 것을 아무도 못 알아챈다. 실제로 그래서 진행 중인 이슈를 착수 가능으로
잘못 안내한 적이 있다 (#65).

출력을 그대로 붙여넣지 말고 읽는 사람에게 필요한 형태로 정리해 보고한다.
여럿을 잠그고 있는 병목은 짚어 준다 — 어디부터 풀어야 할지가 이 목록의 쓸모다.

## 출력 네 덩어리

| 덩어리 | 어떻게 다루나 |
|---|---|
| 착수 가능 | 그대로 보고 |
| 대기 중 | 막고 있는 이슈 번호가 함께 나온다. 병목을 짚어 보고 |
| 정정 필요 | **사람에게 보고만 한다. 임의로 고치지 않는다** |
| 보드에 없는 열린 이슈 | 보고해서 카드를 올리게 한다 |

## 고치지 않는다

**읽기 전용이다.** 배정·Status 변경·브랜치 생성은 `/start-work` 의 일이다.
불일치를 발견해도 보고만 한다 — 누가 무엇을 잡고 있는지는 사람이 안다.

## 스크립트를 고쳐야 할 때

선행 판정은 이 스크립트가 하지 않는다. `.github/scripts/build_dashboard.py` 의
`dependency_report()` 를 불러다 쓴다 — [진행 현황 대시보드](https://kdna-gwangju-1.github.io/NCAIClicker/)가
쓰는 바로 그 함수다. **판정을 이쪽에 다시 구현하지 마라.** 두 벌이 되면 대시보드와 이 목록이
서로 다른 답을 내놓고, 어느 쪽이 맞는지 알 수 없게 된다.

| 증상 | 어디를 보나 |
|---|---|
| 선행을 잘못 읽는다 / 형식을 못 읽는다 | `.github/scripts/build_dashboard.py` 의 `parse_deps()`. 고치면 대시보드도 같이 고쳐진다 |
| 멀쩡한 이슈가 "선행 줄을 읽지 못했다"로 뜬다 | **스크립트보다 이슈 본문을 먼저 의심한다.** 형식은 `docs/GIT_WORKFLOW.md` 3절 |
| Status·담당자 불일치 판정이 이상하다 | `list_work.py` 의 `mismatch_reason()` — 이 스크립트가 더한 유일한 판정이다 |
| `판정 로직을 찾지 못했다` 로 멈춘다 | 대시보드 스크립트가 옮겨졌다. `list_work.py` 의 `DASHBOARD` 경로 |
| 한글이 깨지거나 `UnicodeDecodeError` | Windows 기본 cp949. `encoding="utf-8"` 을 못 박은 곳을 확인한다 |
| `gh` 오류 | `gh auth status` |

고친 뒤에는 **반드시 둘 다 돌린다.**

```bash
python .claude/skills/list-work/list_work.py --selftest
python .github/scripts/test_build_dashboard.py
```
