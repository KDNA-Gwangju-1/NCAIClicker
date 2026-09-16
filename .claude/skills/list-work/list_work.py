#!/usr/bin/env python3
"""착수 가능한 이슈를 칸반 보드 기준으로 골라 낸다.

선행 판정은 **직접 하지 않는다.** `.github/scripts/build_dashboard.py` 의
`dependency_report()` 를 그대로 쓴다. 진행 현황 대시보드가 쓰는 바로 그 함수다.

같은 판정을 두 벌 만들면 한쪽만 고쳐졌을 때 대시보드와 이 스크립트가 서로 다른 답을
내놓는다. 어느 쪽이 맞는지 아무도 모르게 된다 — 규칙을 두 곳에 적지 않는 이유와 같다.

그래서 이 스크립트가 더하는 것은 하나뿐이다:
**Status 와 담당자가 어긋난 이슈를 따로 골라 내는 것.**
대시보드는 선행만 보므로 이걸 못 잡는다. 실제로 담당자만 보고 판정하다가
진행 중인 이슈를 착수 가능으로 잘못 안내한 적이 있다 (#65).

선행 줄의 형식 정본은 docs/GIT_WORKFLOW.md 3절이다.
"""
import importlib.util
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DASHBOARD = REPO_ROOT / ".github" / "scripts" / "build_dashboard.py"


def load_dashboard():
    """대시보드 스크립트를 모듈로 불러온다.

    `.github/scripts` 는 패키지가 아니라 경로로 직접 읽는다.
    build_dashboard.py 는 main() 이 __main__ 가드 안에 있어 import 해도 안전하다.
    """
    if not DASHBOARD.exists():
        sys.exit("판정 로직을 찾지 못했다: %s\n"
                 "대시보드 스크립트가 옮겨졌다면 이 파일의 DASHBOARD 경로를 고친다." % DASHBOARD)
    spec = importlib.util.spec_from_file_location("build_dashboard", DASHBOARD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def issue_of(item):
    return item.get("content") or {}


def assignees_of(item):
    nodes = (issue_of(item).get("assignees") or {}).get("nodes") or []
    return [n["login"] for n in nodes]


def mismatch_reason(status, assignees):
    """착수 절차가 중간에 끊긴 흔적이면 사유를, 아니면 None 을 돌려준다.

    배정과 Status 변경은 별개의 단계라(AGENTS.md "작업 흐름") 한쪽만 하고 멈추면
    이렇게 어긋난다. 조용히 걸러 내면 목록에서 사라질 뿐 문제는 그대로 남는다.
    """
    if status == "In Progress" and not assignees:
        return "In Progress 인데 담당자가 없다"
    if status == "Todo" and assignees:
        return "담당자(%s)가 있는데 Status 가 Todo 다" % ",".join(assignees)
    if not status:
        return "보드에서 Status 가 비어 있다"
    return None


def find_off_board(items):
    """보드에 카드가 없는 열린 이슈. 보드에 없는 작업은 팀에게 존재하지 않는다."""
    on_board = {issue_of(i).get("number") for i in items}
    numbers = subprocess.run(
        ["gh", "issue", "list", "--state", "open", "--limit", "200",
         "--json", "number", "-q", ".[].number"],
        capture_output=True, check=True,
    ).stdout.split()
    return sorted(set(map(int, numbers)) - on_board)


def report(dashboard, items):
    ready, blocked, unparsed, duplicates = dashboard.dependency_report(items)

    # 선행은 작업 번호(2.1)로 적혀 있는데 사람이 찾아가는 것은 이슈 번호(#16)다.
    # 막고 있는 것을 바로 열어 볼 수 있어야 목록이 쓸모가 있다.
    # 번호가 겹칠 수 있으니 카드를 전부 모은다. 하나만 남기면 막고 있는 이슈를
    # 절반만 알려 주게 된다.
    issue_no = {}
    for item in items:
        no = dashboard.task_no(item)
        if no:
            issue_no.setdefault(no, []).append(issue_of(item).get("number"))

    def blocker_label(no):
        if no not in issue_no:
            return "%s(보드에 없음)" % no
        return ", ".join("#%s" % n for n in sorted(issue_no[no]))

    startable, waiting, broken, ongoing = [], [], [], []

    for rows, bucket in ((ready, startable), (blocked, waiting)):
        for task_no, item, waiting_on in rows:
            issue = issue_of(item)
            status = dashboard.field_value(item, "Status")
            assignees = assignees_of(item)
            reason = mismatch_reason(status, assignees)
            if reason:
                broken.append((issue.get("number"), issue.get("title"), reason))
            elif status == "Todo":
                note = (", ".join(blocker_label(w) for w in waiting_on)
                        if waiting_on else "선행 충족")
                bucket.append((issue.get("number"), issue.get("title"), note))
            else:
                # In Progress + 담당자 있음은 정상 진행 중이다. 착수 대상은 아니지만
                # 출력에서 빼 버리면 왜 안 보이는지 알 수 없어 '진행 중'으로 추측하게 된다.
                # 실제로 그렇게 잘못 읽은 적이 있다 — 그래서 따로 찍는다.
                ongoing.append((issue.get("number"), issue.get("title"),
                                "진행 중 · %s" % ", ".join(assignees)))

    for task_no, item, _ in unparsed:
        issue = issue_of(item)
        broken.append((issue.get("number"), issue.get("title"),
                       "선행 줄을 읽지 못했다 — 형식은 docs/GIT_WORKFLOW.md 3절"))

    # 번호 중복은 대시보드가 골라 낸다. 여기서 다시 판정하지 않고 받아서 보고만 한다.
    # 중복 카드는 완료·진행 중일 수도 있어 다른 덩어리와 겹친다. 합계가 어긋나지 않게
    # 겹친 이슈 번호를 따로 돌려준다.
    dup_numbers = set()
    for task_no, cards in duplicates:
        others = [issue_of(c).get("number") for c in cards]
        for card in cards:
            issue = issue_of(card)
            dup_numbers.add(issue.get("number"))
            mates = [n for n in others if n != issue.get("number")]
            broken.append((issue.get("number"), issue.get("title"),
                           "작업 번호 %s 가 #%s 와 겹친다 — 이슈 번호가 앞선 쪽을 원본으로 두고 "
                           "뒤쪽 제목의 번호를 옮긴다"
                           % (task_no, ", #".join(map(str, mates)))))

    return startable, waiting, broken, ongoing, dup_numbers


SELFTEST_CASES = [
    # (Status, 담당자, 정정 필요로 봐야 하나)
    ("Todo", [], False),
    ("Todo", ["kim"], True),
    ("In Progress", ["kim"], False),
    ("In Progress", [], True),
    ("Done", [], False),
    (None, [], True),
]


def selftest():
    """이 스크립트가 더한 부분만 검사한다.

    선행 판정은 여기서 하지 않으므로 검사하지 않는다 —
    그쪽은 .github/scripts/test_build_dashboard.py 가 덮는다. 같이 돌린다.
    """
    failed = 0
    for status, assignees, expected in SELFTEST_CASES:
        reason = mismatch_reason(status, assignees)
        ok = bool(reason) is expected
        failed += 0 if ok else 1
        print("%s Status=%-12s 담당자=%-8s 정정필요=%-5s %s"
              % ("OK  " if ok else "FAIL", status, assignees or "없음",
                 bool(reason), reason or ""))
    print()
    print("%d/%d 통과" % (len(SELFTEST_CASES) - failed, len(SELFTEST_CASES)))
    return 1 if failed else 0


def main():
    if "--selftest" in sys.argv:
        return selftest()

    dashboard = load_dashboard()
    items = dashboard.fetch_items()
    startable, waiting, broken, ongoing, dup_numbers = report(dashboard, items)

    for label, rows in (("착수 가능", startable), ("대기 중", waiting),
                        ("정정 필요", broken), ("진행 중 (착수 대상 아님)", ongoing)):
        print("=== %s (%d건)" % (label, len(rows)))
        for number, title, note in rows:
            print("  #%s %s | %s" % (number, title, note))
    print("=== 보드에 없는 열린 이슈: %s" % (find_off_board(items) or "없음"))

    # 합계를 찍어 두면 어떤 카드가 어느 덩어리로 갔는지 빠짐없이 확인할 수 있다.
    # 수가 맞지 않으면 어딘가 빠진 것이므로, 안 보이는 이슈의 상태를 추측하지 말고
    # `gh issue view <번호>` 로 확인한다.
    cards = [i for i in items if dashboard.task_no(i)]
    done = sum(1 for i in cards
               if (issue_of(i).get("state") == "CLOSED"
                   or dashboard.field_value(i, "Status") == "Done"))
    # 제목에 작업 번호가 없는 카드는 선행 판정 대상이 아니다. 닫힌 규칙·문서 이슈가
    # 대부분이지만 열린 채로 여기 빠지면 목록에서 통째로 사라진다.
    numberless = [issue_of(i).get("number") for i in items if not dashboard.task_no(i)]
    open_numberless = sorted(
        issue_of(i).get("number") for i in items
        if not dashboard.task_no(i) and issue_of(i).get("state") != "CLOSED")
    # 번호 중복은 다른 덩어리와 겹치므로 합에서 빼고 따로 적는다. 그러지 않으면
    # 합계가 카드 수보다 커져 "빠짐없이 분류됐는지" 확인하는 쓸모가 사라진다.
    disjoint = len(broken) - len(dup_numbers)
    print("=== 합계: 보드 카드 %d장 = 번호 있는 %d (착수가능 %d / 대기 %d / 정정필요 %d / "
          "진행중 %d / 완료 %d) + 번호 없는 %d"
          % (len(items), len(cards), len(startable), len(waiting), disjoint,
             len(ongoing), done, len(numberless)))
    if dup_numbers:
        print("    작업 번호 중복 %d장은 위 덩어리와 겹치므로 합에서 뺐다: %s"
              % (len(dup_numbers),
                 ", ".join("#%s" % n for n in sorted(dup_numbers))))
    if open_numberless:
        print("    번호 없는 열린 카드(판정 제외): %s — 제목에 작업 번호를 붙여야 판정된다"
              % ", ".join("#%s" % n for n in open_numberless))
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")  # Windows 기본 cp949 로는 한글 출력이 깨진다
    sys.exit(main())
