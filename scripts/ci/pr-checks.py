#!/usr/bin/env python3
"""QONAQET pr-checks — PR-ın iş prosesinə uyğunluğu (E00-T09, CLAUDE.md → "How Claude Code works").

Giriş (mühit dəyişənləri): PR_TITLE, PR_BODY, PR_AUTHOR, REPO, GITHUB_TOKEN.
Yalnız REST və Python stdlib. Pozuntu varsa 1 ilə çıxır və hər birini annotation kimi yazır.

Qaydalar:
  1. Başlıq Conventional Commit formatındadır: `type(scope)!: təsvir`
  2. Body-də eyni repodakı issue-ya `Closes #n` (close/fix/resolve formaları) var
  3. Hər bağlanan issue açıqdır, `task` label-lidir və valideyni `epic` label-li issue-dur
  4. Valideyn epic PR müəllifinə claim olunub (epic-in assignee-si; `scripts/epic.py claim Exx`).
     `parallel-ok` label-li task-lar istisnadır — onları epic-i saxlamayan developer də götürə bilər.
Dependabot PR-ları 2–3-cü qaydalardan azaddır (onlar task-dan yox, avtomatik yaranır).
"""
from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.request
from typing import Callable

TYPES = ("feat", "fix", "chore", "docs", "ci", "build", "refactor", "perf", "test", "style", "revert")
TITLE = re.compile(rf"^(?:{'|'.join(TYPES)})(?:\([a-z0-9][a-z0-9._/-]*\))?!?: \S.*$")
CLOSES = re.compile(r"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s+#(\d+)\b", re.IGNORECASE)
EXEMPT_AUTHORS = {"dependabot[bot]"}

# path → JSON | None (404). Testlərdə saxta ilə əvəz olunur.
Fetch = Callable[[str], "dict | None"]


def github_fetch(repo: str, token: str) -> Fetch:
    def fetch(path: str) -> dict | None:
        request = urllib.request.Request(
            f"https://api.github.com/repos/{repo}/{path}",
            headers={
                "Authorization": f"Bearer {token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise

    return fetch


def strip_comments(body: str) -> str:
    """Şablondakı `<!-- ... -->` şərhləri və boş `Closes #` sayılmasın."""
    return re.sub(r"<!--.*?-->", "", body or "", flags=re.DOTALL)


def closing_issues(body: str) -> list[int]:
    seen: list[int] = []
    for number in (int(n) for n in CLOSES.findall(strip_comments(body))):
        if number not in seen:
            seen.append(number)
    return seen


def check(title: str, body: str, author: str, fetch: Fetch) -> list[str]:
    errors: list[str] = []
    if not TITLE.match(title.strip()):
        errors.append(
            f"Başlıq Conventional Commit formatında deyil: '{title}'. "
            f"Gözlənilən: `type(scope): təsvir`, type ∈ {{{', '.join(TYPES)}}}."
        )
    if author in EXEMPT_AUTHORS:
        return errors

    issues = closing_issues(body)
    if not issues:
        errors.append("Body-də `Closes #<issue>` yoxdur — hər PR bir task bağlamalıdır.")
    for number in issues:
        issue = fetch(f"issues/{number}")
        if issue is None:
            errors.append(f"#{number} bu repoda tapılmadı.")
            continue
        if issue.get("pull_request"):
            errors.append(f"#{number} issue deyil, PR-dır.")
            continue
        labels = {label["name"] for label in issue.get("labels", [])}
        if "task" not in labels:
            errors.append(f"#{number} `task` label-li deyil (label-lar: {', '.join(sorted(labels)) or 'yoxdur'}).")
        if issue.get("state") != "open":
            errors.append(f"#{number} artıq bağlıdır.")
        parent = fetch(f"issues/{number}/parent")
        if parent is None:
            errors.append(f"#{number} heç bir epic-in sub-issue-su deyil.")
        elif "epic" not in {label["name"] for label in parent.get("labels", [])}:
            errors.append(f"#{number}-in valideyni #{parent.get('number')} `epic` label-li deyil.")
        elif "parallel-ok" not in labels:
            owners = [a["login"] for a in parent.get("assignees") or []]
            key = (re.match(r"^\[(E\d{2})\]", parent.get("title", "")) or [None, "epic"])[1]
            if not owners:
                errors.append(f"{key} (#{parent.get('number')}) heç kimə claim olunmayıb — "
                              f"`scripts/epic.py claim {key}` ilə götür.")
            elif author not in owners:
                errors.append(f"{key} (#{parent.get('number')}) @{owners[0]} tərəfindən işlənir — "
                              f"bu epic-in task-ını götürmə (`scripts/epic.py status`).")
    return errors


def main() -> int:
    title = os.environ.get("PR_TITLE", "")
    body = os.environ.get("PR_BODY", "")
    author = os.environ.get("PR_AUTHOR", "")
    fetch = github_fetch(os.environ["REPO"], os.environ["GITHUB_TOKEN"])
    errors = check(title, body, author, fetch)

    lines = ["## pr-checks", ""]
    if errors:
        lines += [f"- ❌ {e}" for e in errors]
        for e in errors:
            print(f"::error title=pr-checks::{e}")
    else:
        lines.append("✅ Başlıq, `Closes #n` və task → epic əlaqəsi qaydasındadır.")
    text = "\n".join(lines) + "\n"
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write(text)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
