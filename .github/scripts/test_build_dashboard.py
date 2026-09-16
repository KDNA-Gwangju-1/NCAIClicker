import json
import unittest
from types import SimpleNamespace
from unittest.mock import patch

import build_dashboard as dashboard


def make_item(number, task, body, state="OPEN", status="Todo"):
    return {
        "content": {"number": number, "title": task + " 작업", "body": body,
                    "state": state, "url": "https://github.com/example/repo/issues/" + str(number),
                    "assignees": {"nodes": []}},
        "fieldValues": {"nodes": [{"name": status, "field": {"name": "Status"}}]},
    }


class DependencyTests(unittest.TestCase):
    def parse(self, value):
        return dashboard.parse_deps("**선행**: " + value, ["2.2", "2.3", "2.4", "2.5"], {"12": "2.2"})

    def test_explicit_none_and_supported_references(self):
        self.assertEqual(self.parse("없음 — 바로 착수 가능"), ([], True))
        self.assertEqual(self.parse("2.2, #12, 2.3"), (["2.2", "2.3"], True))
        self.assertEqual(self.parse("2.x"), (["2.2", "2.3", "2.4", "2.5"], True))
        self.assertEqual(self.parse("2.2~2.5"), (["2.2", "2.3", "2.4", "2.5"], True))

    def test_invalid_dependencies_never_become_ready(self):
        for value in ["", "오타", "#999", "9.x", "2.2, 오타", "2.2,", "2.5~2.2",
                      "2.2~3.1", "2.2~2.6", "없음, 2.2", "없음인듯", "2.2.3junk"]:
            with self.subTest(value=value):
                self.assertEqual(self.parse(value)[1], False)

    def test_blank_line_cannot_consume_next_field(self):
        body = "**선행**: \r\n**완료 기준**: 없음"
        self.assertEqual(dashboard.parse_deps(body, [], {}), ([], False))

    def test_duplicate_dependency_lines_need_review(self):
        self.assertEqual(self.parse("없음\n**선행**: 2.2"), ([], False))

    def test_missing_task_blocks_and_bad_reference_needs_review(self):
        items = [make_item(1, "1.1", "**선행**: 9.1"),
                 make_item(2, "1.2", "**선행**: #999"),
                 make_item(3, "1.3", "**선행**: 없음")]
        ready, blocked, unparsed, _ = dashboard.dependency_report(items)
        self.assertEqual([row[0] for row in ready], ["1.3"])
        self.assertEqual(blocked[0][2], ["9.1"])
        self.assertEqual([row[0] for row in unparsed], ["1.2"])

    def test_range_waits_for_every_unfinished_task(self):
        items = [make_item(i, "2." + str(i), "**선행**: 없음", "CLOSED" if i != 4 else "OPEN")
                 for i in range(2, 6)]
        items.append(make_item(6, "2.6", "**선행**: 2.2~2.5"))
        _, blocked, _, _ = dashboard.dependency_report(items)
        self.assertEqual(blocked[0][2], ["2.4"])

    def test_duplicate_task_numbers_are_reported_not_overwritten(self):
        items = [make_item(43, "8.2", "**선행**: 없음", "CLOSED", "Done"),
                 make_item(66, "8.2", "**선행**: 없음", "OPEN", "Todo")]
        _, _, _, duplicates = dashboard.dependency_report(items)
        self.assertEqual([n for n, _ in duplicates], ["8.2"])
        self.assertEqual([i["content"]["number"] for i in duplicates[0][1]], [43, 66])

    def test_duplicate_number_is_done_only_when_every_card_is_done(self):
        dupes = [make_item(43, "8.2", "**선행**: 없음", "CLOSED", "Done"),
                 make_item(66, "8.2", "**선행**: 없음", "OPEN", "Todo")]
        follower = make_item(70, "8.6", "**선행**: 8.2")
        _, blocked, _, _ = dashboard.dependency_report(dupes + [follower])
        self.assertEqual([r[2] for r in blocked if r[0] == "8.6"], [["8.2"]])
        # 순서를 뒤집어도 같은 답이 나와야 한다 — 덮어쓰기면 여기서 갈린다.
        _, blocked_rev, _, _ = dashboard.dependency_report(dupes[::-1] + [follower])
        self.assertEqual([r[2] for r in blocked_rev if r[0] == "8.6"], [["8.2"]])
        both_done = [make_item(43, "8.2", "**선행**: 없음", "CLOSED", "Done"),
                     make_item(66, "8.2", "**선행**: 없음", "CLOSED", "Done")]
        ready, _, _, _ = dashboard.dependency_report(both_done + [follower])
        self.assertEqual([r[0] for r in ready], ["8.6"])

    def test_html_warns_about_duplicate_task_numbers(self):
        items = [make_item(43, "8.2", "**선행**: 없음", "CLOSED", "Done"),
                 make_item(66, "8.2", "**선행**: 없음", "OPEN", "Todo")]
        rendered = dashboard.render(items)
        self.assertIn("작업 번호 중복", rendered)
        self.assertNotIn("작업 번호 중복", dashboard.render([make_item(1, "1.1", "**선행**: 없음")]))

    def test_html_keeps_unparsed_warning_and_escapes_titles(self):
        item = make_item(1, "1.1", "**선행**: typo")
        item["content"]["title"] += " <script>"
        rendered = dashboard.render([item])
        self.assertIn("선행 확인 필요", rendered)
        self.assertIn("&lt;script&gt;", rendered)

    @patch.object(dashboard.subprocess, "run")
    def test_fetch_reads_all_pages(self, run):
        def page(nodes, more, cursor):
            return SimpleNamespace(stdout=json.dumps({"data": {"organization": {"projectV2": {
                "items": {"nodes": nodes, "pageInfo": {"hasNextPage": more, "endCursor": cursor}}
            }}}}))
        run.side_effect = [page([{"first": 1}], True, "next"), page([{"second": 2}], False, None)]
        self.assertEqual(dashboard.fetch_items(), [{"first": 1}, {"second": 2}])
        self.assertIn("cursor=next", run.call_args_list[1].args[0])


if __name__ == "__main__":
    unittest.main()
