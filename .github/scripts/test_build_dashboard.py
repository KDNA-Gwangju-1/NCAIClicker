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
        ready, blocked, unparsed = dashboard.dependency_report(items)
        self.assertEqual([row[0] for row in ready], ["1.3"])
        self.assertEqual(blocked[0][2], ["9.1"])
        self.assertEqual([row[0] for row in unparsed], ["1.2"])

    def test_range_waits_for_every_unfinished_task(self):
        items = [make_item(i, "2." + str(i), "**선행**: 없음", "CLOSED" if i != 4 else "OPEN")
                 for i in range(2, 6)]
        items.append(make_item(6, "2.6", "**선행**: 2.2~2.5"))
        _, blocked, _ = dashboard.dependency_report(items)
        self.assertEqual(blocked[0][2], ["2.4"])

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
