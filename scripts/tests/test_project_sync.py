#!/usr/bin/env python3
"""scripts/ci/project-sync.py testləri — yaddaşda saxta Projects v2 board-u ilə.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import os
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "ci" / "project-sync.py"
spec = importlib.util.spec_from_file_location("project_sync", SCRIPT)
ps = importlib.util.module_from_spec(spec)
sys.modules["project_sync"] = ps
spec.loader.exec_module(ps)

OPTIONS = {
    "Status": ["Backlog", "Ready", "In Progress", "In Review", "Done"],
    "Area": ["backend", "integration", "mobile", "web-landing", "web-business", "web-admin", "infra", "design"],
    "Priority": ["p0", "p1", "p2"],
    "Size": ["XS", "S", "M", "L"],
}


def issue(number, title, labels, body="", state="open", pr=False):
    data = {"number": number, "node_id": f"I_{number}", "title": title, "state": state, "body": body,
            "labels": [{"name": n} for n in labels]}
    if pr:
        data["pull_request"] = {}
    return data


class FakeGitHub:
    def __init__(self) -> None:
        self.issues = {
            1: issue(1, "[E00] Repo", ["epic", "area:infra", "p0"]),
            11: issue(11, "[E00-T10] project-sync.yml", ["task", "area:infra", "p0"],
                      body="Context\n\n---\n\n**Valideyn epic:** E00 — Repo\n**Size:** M\n"),
            12: issue(12, "[E00-T11] epic-progress.yml", ["task", "area:design", "p1"], body="**Size:** XS\n"),
            13: issue(13, "[E00-T12] closed", ["task"], state="closed"),
            40: issue(40, "loose bug", ["bug", "area:nonexistent"]),
            187: issue(187, "chore: pr", [], pr=True),
        }
        self.parents = {11: 1, 12: 1, 13: 1}
        self.items: dict[str, dict] = {}   # content id → {"id", values}
        self.mutations: list[str] = []

    def rest(self, path):
        if path.startswith("issues?"):
            page = int(path.rsplit("page=", 1)[1])
            return list(self.issues.values()) if page == 1 else []
        parts = path.split("/")
        number = int(parts[1])
        if len(parts) == 3:
            parent = self.parents.get(number)
            return self.issues[parent] if parent else None
        return self.issues.get(number)

    def gql(self, query, v):
        fields = [{"id": "F_Epic", "name": "Epic"}, {"id": "F_Title", "name": "Title"}]
        fields += [{"id": f"F_{n}", "name": n, "options": [{"id": f"O_{n}_{o}", "name": o} for o in opts]}
                   for n, opts in OPTIONS.items()]
        if query is ps.Q_PROJECTS:
            return {"organization": {"projectsV2": {"nodes": [{"id": "P_old", "title": "Köhnə"}, {"id": "P", "title": "QONAQET"}]}}}
        if query is ps.Q_FIELDS:
            return {"node": {"fields": {"nodes": fields + [{}]}}}
        if query is ps.M_ADD:
            item = self.items.setdefault(v["c"], {"id": f"IT_{v['c']}", "values": {}})
            return {"addProjectV2ItemById": {"item": {"id": item["id"]}}}
        item = next(i for i in self.items.values() if i["id"] == v["i"])
        if query is ps.Q_STATUS:
            name = item["values"].get("Status")
            return {"node": {"fieldValueByName": {"name": name} if name else None}}
        self.mutations.append(v["f"])
        field = v["f"].removeprefix("F_")
        value = v["v"]
        if query is ps.M_OPTION:
            value = value.rsplit("_", 1)[1]
        item["values"][field] = value
        return {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}

    def values(self, number):
        return self.items[f"I_{number}"]["values"]


class BoardTests(unittest.TestCase):
    def setUp(self) -> None:
        self.gh = FakeGitHub()
        self.logs: list[str] = []
        self.board = ps.Board("QonaqEt", "QONAQET", self.gh.rest, self.gh.gql, self.logs.append)

    def test_task_gets_epic_from_parent_and_fields_from_labels_and_body(self) -> None:
        self.board.sync_issue(11)
        self.assertEqual(self.gh.values(11), {"Epic": "E00", "Area": "infra", "Priority": "p0", "Size": "M"})

    def test_epic_uses_its_own_key_and_has_no_size(self) -> None:
        self.board.sync_issue(1)
        self.assertEqual(self.gh.values(1), {"Epic": "E00", "Area": "infra", "Priority": "p0"})

    def test_issue_not_on_board_is_added(self) -> None:
        self.assertNotIn("I_12", self.gh.items)
        self.board.sync_issue(12)
        self.assertEqual(self.gh.values(12)["Area"], "design")

    def test_unknown_option_is_warned_not_fatal_and_pr_is_skipped(self) -> None:
        self.assertEqual(self.board.sync_issue(40)["Area"], "nonexistent")
        self.assertEqual(self.gh.values(40), {})
        self.assertTrue(any("variantı board-da yoxdur" in line for line in self.logs))
        self.assertEqual(self.board.sync_issue(187), {})
        self.assertNotIn("I_187", self.gh.items)

    def test_sync_is_idempotent(self) -> None:
        self.board.sync_issue(11)
        first = dict(self.gh.values(11))
        self.board.sync_issue(11)
        self.assertEqual(self.gh.values(11), first)
        self.assertEqual(len(self.gh.items), 1)

    def test_branch_moves_backlog_task_to_in_progress(self) -> None:
        for ref in ("refs/heads/chore/11-project-sync", "feat/11-x"):
            self.gh.items.clear()
            self.assertEqual(self.board.start_branch(ref), "In Progress")
            self.assertEqual(self.gh.values(11)["Status"], "In Progress")

    def test_branch_never_moves_status_backwards(self) -> None:
        for status in ("In Review", "Done"):
            self.board._item(self.gh.issues[11])
            self.gh.values(11)["Status"] = status
            self.assertEqual(self.board.start_branch("fix/11-y"), status)
            self.assertEqual(self.gh.values(11)["Status"], status)

    def test_branch_from_ready_advances(self) -> None:
        self.board._item(self.gh.issues[11])
        self.gh.values(11)["Status"] = "Ready"
        self.assertEqual(self.board.start_branch("feat/11-z"), "In Progress")

    def test_non_issue_branches_are_ignored(self) -> None:
        for ref in ("main", "claude/dazzling-gates-9yle0s", "feat/no-number", "feat/999-missing", "chore/187-pr", "docs/11-x"):
            self.assertIn(self.board.start_branch(ref), ("ignored",), ref)
        self.assertEqual(self.gh.mutations, [])

    def test_closed_issue_is_not_reopened_on_the_board(self) -> None:
        self.assertEqual(self.board.start_branch("fix/13-late"), "closed")
        self.assertEqual(self.gh.mutations, [])

    def test_backfill_syncs_every_issue_but_not_prs(self) -> None:
        self.assertEqual(self.board.backfill(), 5)
        self.assertEqual(set(self.gh.items), {"I_1", "I_11", "I_12", "I_13", "I_40"})

    def test_missing_project_is_a_clear_error(self) -> None:
        with self.assertRaisesRegex(ps.SyncError, "'YOX' project-i"):
            ps.Board("QonaqEt", "YOX", self.gh.rest, self.gh.gql)


class CliTests(unittest.TestCase):
    def test_missing_token_stops_with_a_clear_message(self) -> None:
        env = {k: v for k, v in os.environ.items() if k != "GH_TOKEN"}
        result = subprocess.run([sys.executable, str(SCRIPT), "issue", "11"], capture_output=True, text=True, env=env)
        self.assertEqual(result.returncode, 1)
        self.assertIn("::error title=project-sync::PROJECT_TOKEN", result.stdout)


if __name__ == "__main__":
    unittest.main()
