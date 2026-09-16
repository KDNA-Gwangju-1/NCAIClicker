#!/usr/bin/env python3
"""착수 가능한 이슈를 칸반 보드 기준으로 판정한다.

판정 규칙의 정본은 AGENTS.md 의 "작업 흐름 (요약)" 절이고,
이 스크립트는 그것을 실행할 뿐이다. 둘이 어긋나면 AGENTS.md 를 따른다.

원칙: 판단이 서지 않으면 통과시키지 않는다.
착수 가능으로 잘못 올리는 쪽이 대기로 잘못 내리는 쪽보다 비싸다 —
사람이 막힌 일을 잡고 나서야 막힌 걸 알게 되기 때문이다.
"""
import json
import re
import subprocess
import sys

PROJECT = "2"
OWNER = "KDNA-Gwangju-1"

# 이슈 제목 앞에 붙은 실행 계획 번호. "3.1 EconomyManager ..." 의 "3.1"
PLAN_IN_TITLE = re.compile(r"^([0-9]+(?:\.[0-9]+)*)\s")
# 계획 밖 이슈의 대괄호 머리말. "[1.x] ...", "[규칙] ...", "[Contract] ..."
BRACKET_HEAD = re.compile(r"^\[([^\]]*)\]\s*(.*)")
# 이슈 본문의 선행 줄. "**선행**: 3.1, 1.3.1"
PREREQ_LINE = re.compile(r"\*\*선행\*\*:\s*(.+)")
# 선행 줄에서 뽑아낼 토큰. "3.1" "1.2.1" "6.x"
PREREQ_TOKEN = re.compile(r"^[0-9]+(?:\.[0-9x]+)*$")


def gh_json(*args):
    out = subprocess.run(args, capture_output=True, check=True).stdout
    return json.loads(out.decode("utf-8"))


def load_board():
    """보드 아이템을 이슈 번호로 색인한다."""
    data = gh_json(
        "gh", "project", "item-list", PROJECT,
        "--owner", OWNER, "--limit", "200", "--format", "json",
    )
    issues = {}
    for item in data["items"]:
        content = item.get("content") or {}
        if content.get("type") != "Issue":
            continue
        issues[content["number"]] = {
            "status": item.get("status"),
            "assignees": item.get("assignees") or [],
            "title": content.get("title") or "",
            "body": content.get("body") or "",
        }
    return issues


def build_plan_index(issues):
    """계획번호 -> (이슈번호, Status). 번호를 못 읽은 이슈는 따로 모은다.

    번호를 못 읽은 이슈가 있으면 묶음 선행(6.x)을 신뢰할 수 없다.
    그 이슈가 묶음의 일원인지 알 수 없어, 빠진 채로 "묶음 전부 완료" 가
    나올 수 있기 때문이다. 그래서 목록을 함께 돌려준다.

    단 Done 인 이슈는 세지 않는다. 어느 묶음에 속하든 막을 수 없으므로
    판정을 흐리지 않는다. 이걸 빼지 않으면 제목이 대괄호로 시작하는
    메타 이슈들 때문에 묶음 선행이 늘 판정 불가로 떨어진다.
    """
    plan = {}
    unreadable = []
    for number, issue in issues.items():
        title = issue["title"]

        # 대괄호 머리말은 벗겨 낸다. "[6.3] 타격 연출" 은 6.3 으로 읽고,
        # "[1.x]" 나 "[규칙]" 처럼 번호를 특정하지 않는 것은 계획 밖 이슈다 —
        # 어느 묶음의 일원도 아니므로 묶음 판정을 흐리지 않는다.
        head = BRACKET_HEAD.match(title)
        if head:
            label, rest = head.group(1), head.group(2)
            if not re.fullmatch(r"[0-9]+(?:\.[0-9]+)*", label):
                continue
            title = "%s %s" % (label, rest)

        match = PLAN_IN_TITLE.match(title)
        if match:
            plan[match.group(1)] = (number, issue["status"])
        elif issue["status"] != "Done":
            unreadable.append(number)
    return plan, unreadable


def check_prereq(token, plan, unreadable):
    """선행 토큰 하나를 판정한다. (통과 여부, 사유) 를 돌려준다.

    통과 여부가 None 이면 판정 불가다. 착수 가능으로 올리지 않는다.
    """
    if token.endswith(".x"):
        if unreadable:
            return None, "묶음 %s — 제목 번호를 못 읽은 이슈(%s)가 있어 판정 불가" % (
                token, ",".join("#%d" % n for n in sorted(unreadable))
            )
        prefix = token[:-1]  # "6.x" -> "6."
        group = [v for k, v in plan.items() if k.startswith(prefix)]
        if not group:
            return None, "묶음 %s 에 해당하는 이슈 없음" % token
        blocking = sorted(n for n, status in group if status != "Done")
        if blocking:
            return False, ",".join("#%d" % n for n in blocking)
        return True, ""

    if token not in plan:
        return None, "선행 %s 에 해당하는 이슈를 찾지 못함" % token
    number, status = plan[token]
    if status != "Done":
        return False, "#%d" % number
    return True, ""


def classify(issues):
    plan, unreadable = build_plan_index(issues)
    ready, waiting, broken = [], [], []

    for number, issue in sorted(issues.items()):
        status = issue["status"]
        assignees = issue["assignees"]
        title = issue["title"]

        if status == "Done":
            continue

        # 착수 절차가 중간에 끊긴 흔적. 착수 가능·불가로 분류하지 않는다.
        if status == "In Progress" and not assignees:
            broken.append((number, title, "In Progress 인데 담당자가 없다"))
            continue
        if status == "Todo" and assignees:
            broken.append((number, title,
                           "담당자(%s)가 있는데 Status 가 Todo 다" % ",".join(assignees)))
            continue

        match = PREREQ_LINE.search(issue["body"])
        if not match:
            broken.append((number, title, "본문에 **선행**: 줄이 없다"))
            continue

        if status != "Todo":
            continue  # 정상 진행 중. 목록에 올리지 않는다.

        line = match.group(1).strip()
        if line.startswith("없음"):
            ready.append((number, title, "선행 없음"))
            continue

        tokens = [t for t in re.split(r"[,\s]+", line) if PREREQ_TOKEN.match(t)]
        if not tokens:
            broken.append((number, title, "선행 줄에서 번호를 읽지 못했다: %s" % line))
            continue

        blockers, unknown = [], []
        for token in tokens:
            passed, reason = check_prereq(token, plan, unreadable)
            if passed is None:
                unknown.append(reason)
            elif not passed:
                blockers.append(reason)

        if unknown:
            broken.append((number, title, "; ".join(unknown)))
        elif blockers:
            waiting.append((number, title, ", ".join(blockers)))
        else:
            ready.append((number, title, "선행 %s 완료" % line))

    return ready, waiting, broken


def find_off_board(issues):
    numbers = subprocess.run(
        ["gh", "issue", "list", "--state", "open", "--limit", "200",
         "--json", "number", "-q", ".[].number"],
        capture_output=True, check=True,
    ).stdout.split()
    return sorted(set(map(int, numbers)) - set(issues))


SELFTEST_CASES = [
    # (설명, 보드에 있다고 가정할 이슈들, 판정할 선행 토큰, 기대 결과)
    ("묶음이 전부 Done", [("6.1 HUD", "Done"), ("6.2 결과", "Done")], "6.x", True),
    ("묶음에 미완이 있음", [("6.1 HUD", "Done"), ("6.2 결과", "Todo")], "6.x", False),
    ("묶음에 제목 깨진 열린 이슈", [("6.1 HUD", "Done"), ("타격 연출", "Todo")], "6.x", None),
    ("제목 깨졌지만 Done 이라 무해", [("6.1 HUD", "Done"), ("[규칙] 뭔가", "Done")], "6.x", True),
    ("대괄호 안이 번호면 읽는다", [("6.1 HUD", "Done"), ("[6.2] 결과", "Todo")], "6.x", False),
    ("대괄호 안이 1.x 면 계획 밖", [("6.1 HUD", "Done"), ("[1.x] 규칙", "Todo")], "6.x", True),
    ("없는 선행 번호", [("6.1 HUD", "Done")], "9.9", None),
    ("단일 선행 미완", [("3.1 Economy", "Todo")], "3.1", False),
    ("단일 선행 완료", [("3.1 Economy", "Done")], "3.1", True),
]


def selftest():
    """판정 규칙이 의도대로 동작하는지 확인한다.

    스크립트를 고친 뒤에는 반드시 이걸 돌린다. 특히 세 번째 경우 —
    묶음 안에 제목 번호를 못 읽는 열린 이슈가 있을 때 True 가 나오면
    막힌 일을 착수 가능으로 올리게 된다. 가장 비싼 오답이다.
    """
    failed = 0
    for label, rows, token, expected in SELFTEST_CASES:
        issues = {
            i + 1: {"status": status, "assignees": [], "title": title, "body": ""}
            for i, (title, status) in enumerate(rows)
        }
        plan, unreadable = build_plan_index(issues)
        actual, reason = check_prereq(token, plan, unreadable)
        mark = "OK  " if actual is expected else "FAIL"
        if actual is not expected:
            failed += 1
        print("%s %-28s 기대=%-5s 실제=%-5s %s" % (mark, label, expected, actual, reason))
    print()
    print("%d/%d 통과" % (len(SELFTEST_CASES) - failed, len(SELFTEST_CASES)))
    return 1 if failed else 0


def main():
    if "--selftest" in sys.argv:
        return selftest()

    issues = load_board()
    ready, waiting, broken = classify(issues)
    off_board = find_off_board(issues)

    for label, rows in (("착수 가능", ready), ("대기 중", waiting), ("정정 필요", broken)):
        print("=== %s (%d건)" % (label, len(rows)))
        for number, title, note in rows:
            print("  #%d %s | %s" % (number, title, note))
    print("=== 보드에 없는 열린 이슈: %s" % (off_board or "없음"))
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")  # Windows 기본 cp949 로는 출력이 깨진다
    sys.exit(main())
