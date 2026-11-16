#!/usr/bin/env python3
"""scripts/ci/pr-checks.py testləri — saxta epic məlumatı ilə (şəbəkə yoxdur).

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import re
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("pr_checks", ROOT / "scripts" / "ci" / "pr-checks.py")
pc = importlib.util.module_from_spec(spec)
sys.modules["pr_checks"] = pc
spec.loader.exec_module(pc)


def task(key, status="todo", labels=()):
    return {"key": key, "status": status, "extra_labels": list(labels)}


def epic(eid, owner="", tasks=()):
    return {"id": eid, "owner": owner, "tasks": list(tasks)}


HEAD = {
    "E00": epic("E00", "u", [task("E00-T05"), task("E00-T10", "dropped")]),
    "E01": epic("E01", "other", [task("E01-T01"), task("E01-T02", labels=["parallel-ok"])]),
    "E02": epic("E02", "", [task("E02-T01")]),
}
BASE = {"E00": epic("E00", "u"), "E01": epic("E01", "other"), "E02": epic("E02", "")}

GOOD_BODY = "## Xülasə\n\nNəsə.\n\nTask: E00-T05\n"


def run(title="feat: x", body=GOOD_BODY, author="u", head=HEAD, base=BASE):
    return pc.check(title, body, author, head, base)


class TitleTests(unittest.TestCase):
    def test_valid_titles(self) -> None:
        for title in ("feat(gifts): partial voucher redemption", "fix: typo", "chore(infra)!: drop x",
                      "ci(backend): add workflow", "docs(adr): 0003 something"):
            self.assertEqual(run(title=title), [], title)

    def test_invalid_titles(self) -> None:
        for title in ("Add docker compose", "feat:missing space", "Feat(x): capital", "feature(x): unknown type",
                      "feat(): empty scope", "feat(x):  ", "build(deps)(deps): bump"):
            errors = run(title=title)
            self.assertEqual(len(errors), 1, title)
            self.assertIn("Conventional Commit", errors[0])


class TaskReferenceTests(unittest.TestCase):
    def test_task_line_forms(self) -> None:
        self.assertEqual(pc.task_keys("Task: E00-T05\nTasks: E01-T01, E01-T02\ntask:E00-T05"),
                         ["E00-T05", "E01-T01", "E01-T02"])

    def test_template_placeholder_and_comments_do_not_count(self) -> None:
        body = "<!-- Task: E00-T05 nümunədir -->\nTask: Exx-Tnn\n"
        self.assertEqual(pc.task_keys(body), [])
        errors = run(body=body)
        self.assertEqual(len(errors), 1)
        self.assertIn("Task: Exx-Tnn", errors[0])

    def test_mentions_without_task_prefix_do_not_count(self) -> None:
        self.assertEqual(pc.task_keys("E00-T05-ə bax"), [])

    def test_unknown_and_dropped_tasks(self) -> None:
        self.assertEqual(run(body="Task: E00-T99"), ["E00-T99 docs/epics-də tapılmadı."])
        self.assertEqual(run(body="Task: E00-T10"), ["E00-T10 ləğv olunub (`status: dropped`)."])


class OwnershipTests(unittest.TestCase):
    def test_own_epic_passes(self) -> None:
        self.assertEqual(run(), [])

    def test_someone_elses_epic_fails(self) -> None:
        errors = run(body="Task: E01-T01")
        self.assertTrue(any("E01 @other tərəfindən işlənir" in e for e in errors), errors)

    def test_parallel_ok_task_may_be_taken(self) -> None:
        self.assertEqual(run(body="Task: E01-T02"), [])

    def test_unclaimed_epic_must_be_claimed_in_the_pr(self) -> None:
        errors = run(body="Task: E02-T01")
        self.assertEqual(len(errors), 1)
        self.assertIn("scripts/epic.py claim E02", errors[0])
        claimed = dict(HEAD, E02=epic("E02", "u", [task("E02-T01")]))
        self.assertEqual(run(body="Task: E02-T01", head=claimed), [])

    def test_taking_over_an_epic_owned_on_main_fails(self) -> None:
        stolen = dict(HEAD, E01=epic("E01", "u", [task("E01-T01")]))
        errors = run(body="Task: E01-T01", head=stolen)
        self.assertEqual(errors, ["main-də E01 @other-ə məxsusdur — onu bu PR-da dəyişmək olmaz."])

    def test_dependabot_is_exempt_from_task_rules_but_not_title(self) -> None:
        self.assertEqual(run(title="build(deps): bump x", body="", author="dependabot[bot]"), [])
        self.assertEqual(len(run(title="Bump x from 1 to 2", body="", author="dependabot[bot]")), 1)

    def test_base_is_read_lazily(self) -> None:
        calls = []
        pc.check("feat: x", GOOD_BODY, "u", HEAD, lambda: calls.append(1) or BASE)
        pc.check("feat: x", "", "dependabot[bot]", HEAD, lambda: calls.append(1) or BASE)
        self.assertEqual(calls, [1])


class RepoTests(unittest.TestCase):
    def test_base_epics_reads_a_real_commit(self) -> None:
        sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True).stdout.strip()
        epics = pc.base_epics(sha)
        self.assertIn("E00", epics)
        self.assertTrue(epics["E00"]["tasks"])

    def test_pr_template_carries_the_task_line(self) -> None:
        template = (ROOT / ".github/pull_request_template.md").read_text(encoding="utf-8")
        self.assertRegex(template, r"(?m)^Task: Exx-Tnn$")

    def test_every_area_label_is_covered_by_labeler(self) -> None:
        configured = re.findall(r"^(area:[a-z-]+):", (ROOT / ".github/labeler.yml").read_text(encoding="utf-8"), re.M)
        areas = ("backend", "integration", "mobile", "web-landing", "web-business", "web-admin", "infra", "design")
        self.assertEqual(sorted(configured), sorted(f"area:{a}" for a in areas))


if __name__ == "__main__":
    unittest.main()
