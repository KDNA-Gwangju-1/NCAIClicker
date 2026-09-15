#!/usr/bin/env python3
"""Fetch the KDNA-Gwangju-1/NCAIClicker project board via GraphQL and render a static HTML kanban dashboard."""
import html
import json
import os
import subprocess
import sys
from datetime import datetime, timezone

OWNER = "KDNA-Gwangju-1"
PROJECT_NUMBER = 2
REPO = "KDNA-Gwangju-1/NCAIClicker"
PROJECT_URL = f"https://github.com/orgs/{OWNER}/projects/{PROJECT_NUMBER}"

COLUMNS = ["Ready", "In Progress", "In Review", "Done", "Blocked"]
COLUMN_COLORS = {
    "Ready": "#6b7280",
    "In Progress": "#d97706",
    "In Review": "#7c3aed",
    "Done": "#16a34a",
    "Blocked": "#dc2626",
}

QUERY = """
query($owner: String!, $number: Int!) {
  organization(login: $owner) {
    projectV2(number: $number) {
      title
      items(first: 100) {
        nodes {
          content {
            ... on Issue {
              title
              number
              url
              state
              assignees(first: 5) { nodes { login } }
            }
          }
          fieldValues(first: 20) {
            nodes {
              ... on ProjectV2ItemFieldSingleSelectValue {
                name
                field { ... on ProjectV2FieldCommon { name } }
              }
            }
          }
        }
      }
    }
  }
}
"""


def fetch_items():
    result = subprocess.run(
        ["gh", "api", "graphql", "-f", f"query={QUERY}", "-F", f"owner={OWNER}", "-F", f"number={PROJECT_NUMBER}"],
        capture_output=True, text=True, check=True,
    )
    data = json.loads(result.stdout)
    return data["data"]["organization"]["projectV2"]["items"]["nodes"]


def field_value(item, field_name):
    for fv in item["fieldValues"]["nodes"]:
        if fv.get("field", {}).get("name") == field_name:
            return fv.get("name")
    return None


def render(items):
    columns = {name: [] for name in COLUMNS}
    for item in items:
        content = item.get("content")
        if not content:
            continue
        status = field_value(item, "Status") or "Ready"
        columns.setdefault(status, []).append(item)

    total = sum(len(v) for v in columns.values())
    done = len(columns.get("Done", []))
    pct = round(done / total * 100) if total else 0
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")

    def card(item):
        c = item["content"]
        priority = field_value(item, "우선순위")
        assignees = ", ".join(a["login"] for a in c["assignees"]["nodes"]) or "미배정"
        badge = f'<span class="badge">{html.escape(priority)}</span>' if priority else ""
        return f"""
        <a class="card" href="{html.escape(c['url'])}" target="_blank" rel="noopener">
          <div class="card-title">#{c['number']} {html.escape(c['title'])}</div>
          <div class="card-meta">{badge}<span class="assignee">{html.escape(assignees)}</span></div>
        </a>"""

    columns_html = ""
    for name in COLUMNS:
        cards = "".join(card(i) for i in columns.get(name, []))
        color = COLUMN_COLORS.get(name, "#6b7280")
        columns_html += f"""
        <div class="column">
          <div class="column-header" style="border-top-color:{color}">
            <span>{html.escape(name)}</span>
            <span class="count">{len(columns.get(name, []))}</span>
          </div>
          <div class="column-body">{cards or '<div class="empty">비어 있음</div>'}</div>
        </div>"""

    return f"""<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NCAIClicker 진행 현황</title>
<style>
  :root {{ color-scheme: light dark; }}
  body {{ margin:0; padding:24px 16px; background:#f7f7f8; color:#1f2328; font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Pretendard,sans-serif; }}
  @media (prefers-color-scheme: dark) {{ body {{ background:#0d1117; color:#e6edf3; }} }}
  h1 {{ font-size:1.25rem; margin:0 0 4px; }}
  .sub {{ color:#6b7280; font-size:0.85rem; margin-bottom:20px; }}
  .sub a {{ color:inherit; }}
  .progress-wrap {{ background:#e5e7eb; border-radius:999px; height:10px; overflow:hidden; margin-bottom:24px; max-width:480px; }}
  @media (prefers-color-scheme: dark) {{ .progress-wrap {{ background:#30363d; }} }}
  .progress-bar {{ height:100%; background:#16a34a; border-radius:999px; }}
  .board {{ display:flex; gap:16px; overflow-x:auto; padding-bottom:8px; }}
  .column {{ flex:0 0 240px; background:#ffffff; border-radius:10px; box-shadow:0 1px 2px rgba(0,0,0,0.06); display:flex; flex-direction:column; max-height:80vh; }}
  @media (prefers-color-scheme: dark) {{ .column {{ background:#161b22; }} }}
  .column-header {{ display:flex; justify-content:space-between; align-items:center; padding:10px 12px; font-weight:600; font-size:0.9rem; border-top:3px solid; border-radius:10px 10px 0 0; }}
  .count {{ background:#eef0f2; border-radius:999px; padding:1px 8px; font-size:0.75rem; color:#57606a; }}
  @media (prefers-color-scheme: dark) {{ .count {{ background:#21262d; color:#8b949e; }} }}
  .column-body {{ padding:8px; overflow-y:auto; display:flex; flex-direction:column; gap:8px; }}
  .card {{ display:block; background:#f6f8fa; border-radius:8px; padding:8px 10px; text-decoration:none; color:inherit; border:1px solid #d0d7de; }}
  @media (prefers-color-scheme: dark) {{ .card {{ background:#0d1117; border-color:#30363d; }} }}
  .card-title {{ font-size:0.85rem; line-height:1.35; margin-bottom:6px; }}
  .card-meta {{ display:flex; justify-content:space-between; align-items:center; font-size:0.72rem; color:#57606a; }}
  @media (prefers-color-scheme: dark) {{ .card-meta {{ color:#8b949e; }} }}
  .badge {{ background:#ddf4ff; color:#0969da; border-radius:999px; padding:1px 6px; }}
  @media (prefers-color-scheme: dark) {{ .badge {{ background:#0d419d33; color:#79c0ff; }} }}
  .empty {{ color:#8b949e; font-size:0.8rem; padding:12px 4px; text-align:center; }}
</style>
</head>
<body>
  <h1>NCAIClicker 진행 현황</h1>
  <div class="sub">{done}/{total} 완료 ({pct}%) · 마지막 갱신 {now} · <a href="{PROJECT_URL}" target="_blank" rel="noopener">칸반 보드 원본 열기</a></div>
  <div class="progress-wrap"><div class="progress-bar" style="width:{pct}%"></div></div>
  <div class="board">{columns_html}</div>
</body>
</html>"""


def main():
    items = fetch_items()
    html_out = render(items)
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "_site"
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write(html_out)
    print(f"wrote {out_dir}/index.html ({len(items)} items)")


if __name__ == "__main__":
    main()
