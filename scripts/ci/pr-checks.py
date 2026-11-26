#!/usr/bin/env python3
"""QONAQET pr-checks — PR-ın iş prosesinə uyğunluğu (CLAUDE.md → "How Claude Code works", ADR 0004).

Giriş (mühit dəyişənləri): PR_TITLE, PR_BODY, PR_AUTHOR, BASE_SHA. İş qovluğu PR-ın checkout-udur.
GitHub API işlədilmir — task-lar `docs/epics/*.md`-dədir. Yalnız Python stdlib.

Qaydalar:
  1. Başlıq Conventional Commit formatındadır: `type(scope)!: təsvir`
  2. Body-də ən azı bir `Task: Exx-Tnn` sətri var
  3. Hər task PR-ın versiyasında mövcuddur və `dropped` deyil
  4. Task-ın epic-i PR müəllifinindir (`owner`) — `parallel-ok` task-lar istisnadır
  5. Base-də (main) epic başqasına məxsus deyil — başqasının epic-ini "ələ keçirmək" olmaz
Dependabot PR-ları 2–5-ci qaydalardan azaddır.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Callable

ROOT = Path(__file__).resolve().parents[2]
PARSER = ROOT / "scripts" / "lib" / "parse-epics.py"
TYPES = ("feat", "fix", "chore", "docs", "ci", "build", "refactor", "perf", "test", "style", "revert")
TITLE = re.compile(rf"^(?:{'|'.join(TYPES)})(?:\([a-z0-9][a-z0-9._/-]*\))?!?: \S.*$")
TASK = re.compile(r"^\s*Tasks?:\s*(.+)$", re.IGNORECASE | re.MULTILINE)
KEY = re.compile(r"\bE\d{2}-T\d{2}\b")
EXEMPT_AUTHORS = {"dependabot[bot]"}

Epics = dict[str, dict]  # epic id → parse-epics çıxışı


def parse_dir(directory: Path) -> Epics:
    out = subprocess.run([sys.executable, str(PARSER), str(directory)], check=True, capture_output=True, encoding="utf-8").stdout
    return {e["id"]: e for e in json.loads(out)}


def base_epics(base_sha: str, repo: Path = ROOT) -> Epics:
    """Base commit-dəki epic faylları (main-də kimin nəyə sahib olduğu)."""
    names = subprocess.run(["git", "ls-tree", "--name-only", base_sha, "docs/epics/"], cwd=repo,
                           check=True, capture_output=True, text=True).stdout.split()
    with tempfile.TemporaryDirectory() as tmp:
        for name in names:
            if re.fullmatch(r"docs/epics/E\d{2}\.md", name):
                text = subprocess.run(["git", "show", f"{base_sha}:{name}"], cwd=repo, check=True,
                                      capture_output=True, encoding="utf-8").stdout
                Path(tmp, Path(name).name).write_text(text, encoding="utf-8")
        return parse_dir(Path(tmp)) if any(Path(tmp).iterdir()) else {}


def strip_comments(body: str) -> str:
    return re.sub(r"<!--.*?-->", "", body or "", flags=re.DOTALL)


def task_keys(body: str) -> list[str]:
    keys: list[str] = []
    for line in TASK.findall(strip_comments(body)):
        for key in KEY.findall(line):
            if key not in keys:
                keys.append(key)
    return keys


def check(title: str, body: str, author: str, head: Epics, base: Epics | Callable[[], Epics]) -> list[str]:
    errors: list[str] = []
    if not TITLE.match(title.strip()):
        errors.append(
            f"Başlıq Conventional Commit formatında deyil: '{title}'. "
            f"Gözlənilən: `type(scope): təsvir`, type ∈ {{{', '.join(TYPES)}}}."
        )
    if author in EXEMPT_AUTHORS:
        return errors

    keys = task_keys(body)
    if not keys:
        errors.append("Body-də `Task: Exx-Tnn` sətri yoxdur — hər PR docs/epics-dəki bir task-a aiddir.")
        return errors
    base_data = base() if callable(base) else base
    for key in keys:
        epic = head.get(key.split("-")[0])
        task = next((t for t in (epic or {}).get("tasks", []) if t["key"] == key), None)
        if task is None:
            errors.append(f"{key} docs/epics-də tapılmadı.")
            continue
        if task["status"] == "dropped":
            errors.append(f"{key} ləğv olunub (`status: dropped`).")
            continue
        if "parallel-ok" in task["extra_labels"]:
            continue
        owner = epic["owner"]
        if not owner:
            errors.append(f"{epic['id']} heç kimə məxsus deyil — `scripts/epic.py claim {epic['id']}` ilə götür və PR-a daxil et.")
        elif owner != author:
            errors.append(f"{epic['id']} @{owner} tərəfindən işlənir — bu epic-in task-ını götürmə (`scripts/epic.py status`).")
        base_owner = (base_data.get(epic["id"]) or {}).get("owner", "")
        if base_owner and base_owner != author:
            errors.append(f"main-də {epic['id']} @{base_owner}-ə məxsusdur — onu bu PR-da dəyişmək olmaz.")
    return errors


def main() -> int:
    title = os.environ.get("PR_TITLE", "")
    body = os.environ.get("PR_BODY", "")
    author = os.environ.get("PR_AUTHOR", "")
    base_sha = os.environ.get("BASE_SHA", "")
    errors = check(title, body, author, parse_dir(ROOT / "docs" / "epics"),
                   (lambda: base_epics(base_sha)) if base_sha else {})

    lines = ["## pr-checks", ""]
    if errors:
        lines += [f"- ❌ {e}" for e in errors]
        for e in errors:
            print(f"::error title=pr-checks::{e}")
    else:
        lines.append("✅ Başlıq, `Task:` və epic sahibliyi qaydasındadır.")
    text = "\n".join(lines) + "\n"
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write(text)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
