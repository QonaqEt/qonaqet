#!/usr/bin/env python3
"""scripts/ci/pr-checks.py testləri — saxta GitHub API ilə.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("pr_checks", ROOT / "scripts" / "ci" / "pr-checks.py")
pc = importlib.util.module_from_spec(spec)
sys.modules["pr_checks"] = pc
spec.loader.exec_module(pc)


def issue(number, labels, state="open", pr=False):
    data = {"number": number, "state": state, "labels": [{"name": n} for n in labels]}
    if pr:
        data["pull_request"] = {"url": "x"}
    return data


ISSUES = {
    "issues/1": issue(1, ["epic", "area:infra"]),
    "issues/3": issue(3, ["task", "area:infra", "p0"]),
    "issues/3/parent": issue(1, ["epic"]),
    "issues/4": issue(4, ["task"], state="closed"),
    "issues/4/parent": issue(1, ["epic"]),
    "issues/5": issue(5, ["bug"]),
    "issues/5/parent": issue(1, ["epic"]),
    "issues/6": issue(6, ["task"]),  # valideyni yoxdur
    "issues/7": issue(7, ["task"]),
    "issues/7/parent": issue(3, ["task"]),
    "issues/187": issue(187, [], pr=True),
}


def fetch(path):
    return ISSUES.get(path)


GOOD_BODY = "## Xülasə\n\nNəsə.\n\nCloses #3\n"


class TitleTests(unittest.TestCase):
    def test_valid_titles(self) -> None:
        for title in ("feat(gifts): partial voucher redemption", "fix: typo", "chore(infra)!: drop x",
                      "ci(backend): add workflow", "docs(adr): 0003 something"):
            self.assertEqual(pc.check(title, GOOD_BODY, "u", fetch), [], title)

    def test_invalid_titles(self) -> None:
        for title in ("Add docker compose", "feat:missing space", "Feat(x): capital", "feature(x): unknown type",
                      "feat(): empty scope", "feat(x):  "):
            errors = pc.check(title, GOOD_BODY, "u", fetch)
            self.assertEqual(len(errors), 1, title)
            self.assertIn("Conventional Commit", errors[0])


class ClosesTests(unittest.TestCase):
    def test_keyword_forms(self) -> None:
        self.assertEqual(pc.closing_issues("closes #3, Fixes #4\nRESOLVED: #5 and resolves #3"), [3, 4, 5])

    def test_template_placeholder_and_comments_do_not_count(self) -> None:
        body = "<!-- Closes #3 nümunədir -->\nCloses #\n"
        self.assertEqual(pc.closing_issues(body), [])
        errors = pc.check("feat: x", body, "u", fetch)
        self.assertEqual(len(errors), 1)
        self.assertIn("Closes #", errors[0])

    def test_mentions_without_keyword_do_not_count(self) -> None:
        self.assertEqual(pc.closing_issues("see #3, related to #4"), [])


class LinkedIssueTests(unittest.TestCase):
    def errors(self, body):
        return pc.check("feat: x", body, "u", fetch)

    def test_open_task_under_epic_passes(self) -> None:
        self.assertEqual(self.errors("Closes #3"), [])

    def test_not_a_task(self) -> None:
        self.assertTrue(any("`task` label-li deyil" in e for e in self.errors("Closes #5")))

    def test_closed_task(self) -> None:
        self.assertEqual(self.errors("Closes #4"), ["#4 artıq bağlıdır."])

    def test_task_without_parent(self) -> None:
        self.assertEqual(self.errors("Closes #6"), ["#6 heç bir epic-in sub-issue-su deyil."])

    def test_parent_is_not_an_epic(self) -> None:
        self.assertEqual(self.errors("Closes #7"), ["#7-in valideyni #3 `epic` label-li deyil."])

    def test_missing_issue_and_pull_request(self) -> None:
        self.assertEqual(self.errors("Closes #999"), ["#999 bu repoda tapılmadı."])
        self.assertEqual(self.errors("Closes #187"), ["#187 issue deyil, PR-dır."])

    def test_every_closed_issue_is_checked(self) -> None:
        errors = self.errors("Closes #3\nCloses #6")
        self.assertEqual(errors, ["#6 heç bir epic-in sub-issue-su deyil."])

    def test_dependabot_is_exempt_from_issue_rules_but_not_title(self) -> None:
        self.assertEqual(pc.check("build(deps): bump x", "", "dependabot[bot]", fetch), [])
        self.assertEqual(len(pc.check("Bump x from 1 to 2", "", "dependabot[bot]", fetch)), 1)



class LabelerConfigTests(unittest.TestCase):
    def test_every_area_label_exists_and_is_covered(self) -> None:
        import re

        configured = re.findall(r"^(area:[a-z-]+):", (ROOT / ".github/labeler.yml").read_text(encoding="utf-8"), re.M)
        areas = ("backend", "integration", "mobile", "web-landing", "web-business", "web-admin", "infra", "design")
        self.assertEqual(sorted(configured), sorted(f"area:{a}" for a in areas))
        bootstrap = (ROOT / "scripts/bootstrap-github.sh").read_text(encoding="utf-8")
        for label in configured:
            self.assertIn(label, bootstrap, f"{label} bootstrap-da yaradılmır")


if __name__ == "__main__":
    unittest.main()
