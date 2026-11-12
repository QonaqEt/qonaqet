#!/usr/bin/env python3
"""scripts/epic.py və epic `depends:` testləri — yaddaşda saxta GitHub ilə.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("epic_claims", ROOT / "scripts" / "epic.py")
ec = importlib.util.module_from_spec(spec)
sys.modules["epic_claims"] = ec
spec.loader.exec_module(ec)

DEPENDS = {"E00": [], "E01": [], "E02": ["E01"], "E03": ["E01"], "E05": ["E02", "E03"]}


class FakeGitHub:
    def __init__(self) -> None:
        self.epics = {
            "E00": {"number": 1, "state": "closed", "assignees": ["ana"]},
            "E01": {"number": 17, "state": "open", "assignees": []},
            "E02": {"number": 34, "state": "open", "assignees": []},
            "E03": {"number": 42, "state": "open", "assignees": []},
            "E05": {"number": 55, "state": "open", "assignees": []},
        }
        self.events: dict[int, list[dict]] = {}
        self.comments: list[tuple[int, str]] = []
        self.racer: str | None = None  # claim anında bizdən əvvəl təyin olunan

    def by_number(self, number):
        return next((k, e) for k, e in self.epics.items() if e["number"] == number)

    def issue(self, key):
        e = self.epics[key]
        return {"number": e["number"], "title": f"[{key}] Epic {key}", "state": e["state"],
                "labels": [{"name": "epic"}], "assignees": [{"login": a} for a in e["assignees"]],
                "sub_issues_summary": {"total": 4, "completed": 4 if e["state"] == "closed" else 1}}

    def __call__(self, method, path, body):
        base, _, query = path.partition("?")
        page = int(query.rsplit("page=", 1)[1]) if "page=" in query else 1
        if base == "issues":
            return 200, ([self.issue(k) for k in self.epics] if page == 1 else [])
        parts = base.split("/")
        key, epic = self.by_number(int(parts[1]))
        if len(parts) == 2:
            return 200, self.issue(key)
        if parts[2] == "assignees":
            for login in body["assignees"]:
                if method == "POST" and login not in epic["assignees"]:
                    if self.racer:
                        epic["assignees"].append(self.racer)
                        self.events.setdefault(epic["number"], []).append(
                            {"event": "assigned", "assignee": {"login": self.racer}})
                    epic["assignees"].append(login)
                    self.events.setdefault(epic["number"], []).append({"event": "assigned", "assignee": {"login": login}})
                elif method == "DELETE" and login in epic["assignees"]:
                    epic["assignees"].remove(login)
            return 201, self.issue(key)
        if parts[2] == "events":
            return 200, self.events.get(epic["number"], []) if page == 1 else []
        if parts[2] == "comments":
            self.comments.append((epic["number"], body["body"]))
            return 201, {}
        raise AssertionError(path)


def claims(gh, user="zahid"):
    return ec.Claims(gh, user, DEPENDS, log=lambda _: None)


class ClaimTests(unittest.TestCase):
    def setUp(self) -> None:
        self.gh = FakeGitHub()

    def test_claim_assigns_and_announces(self) -> None:
        claims(self.gh).claim("E01")
        self.assertEqual(self.gh.epics["E01"]["assignees"], ["zahid"])
        self.assertIn("@zahid tərəfindən götürüldü", self.gh.comments[-1][1])

    def test_claim_is_idempotent_for_the_owner(self) -> None:
        claims(self.gh).claim("E01")
        claims(self.gh).claim("E01")
        self.assertEqual(self.gh.epics["E01"]["assignees"], ["zahid"])
        self.assertEqual(len(self.gh.comments), 1)

    def test_epic_held_by_someone_else_cannot_be_claimed(self) -> None:
        claims(self.gh, "ali").claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "@ali tərəfindən işlənir"):
            claims(self.gh).claim("E01")

    def test_one_epic_per_developer(self) -> None:
        self.gh.epics["E02"]["state"] = "open"
        claims(self.gh).claim("E01")
        self.gh.epics["E01"]["state"] = "closed"  # E02 açılsın deyə
        self.gh.epics["E01"]["assignees"] = ["zahid"]
        claims(self.gh).claim("E02")  # bağlı epic sayılmır
        with self.assertRaisesRegex(ec.ClaimError, "səndə artıq E02 var"):
            claims(self.gh).claim("E03")

    def test_dependencies_must_be_closed(self) -> None:
        with self.assertRaisesRegex(ec.ClaimError, "E05 hələ E02, E03"):
            claims(self.gh).claim("E05")
        with self.assertRaisesRegex(ec.ClaimError, "E00 bağlıdır"):
            claims(self.gh).claim("E00")

    def test_simultaneous_claim_backs_off_for_the_second(self) -> None:
        self.gh.racer = "ali"
        with self.assertRaisesRegex(ec.ClaimError, "@ali götürdü"):
            claims(self.gh).claim("E01")
        self.assertEqual(self.gh.epics["E01"]["assignees"], ["ali"])
        self.assertEqual(self.gh.comments, [])

    def test_release(self) -> None:
        claims(self.gh).claim("E01")
        claims(self.gh).release("E01")
        self.assertEqual(self.gh.epics["E01"]["assignees"], [])
        with self.assertRaisesRegex(ec.ClaimError, "səndə deyil"):
            claims(self.gh).release("E01")

    def test_next_and_status(self) -> None:
        self.assertEqual(claims(self.gh).next().key, "E01")
        claims(self.gh, "ali").claim("E01")
        self.assertIsNone(claims(self.gh).next())  # E02/E03 E01-i gözləyir
        table = claims(self.gh).status()
        self.assertIn("| E01 #17 | 🔒 işlənir | @ali | 1/4 | — |", table)
        self.assertIn("| E05 #55 | ⏳ gözləyir | — | 1/4 | E02, E03 |", table)
        self.assertIn("| E00 #1 | ✅ bağlı |", table)


class DependsParsingTests(unittest.TestCase):
    def parse(self, files: dict[str, str]):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp)
        for name, depends in files.items():
            (tmp / f"{name}.md").write_text(
                f"---\nid: {name}\ntitle: T\narea: infra\npriority: p0\ndepends: {depends}\n---\n\n## Tasks\n\n"
                f"### {name}-T01 · x\n- area: infra\n- priority: p0\n- size: S\n\nBody\n", encoding="utf-8")
        return subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py"), str(tmp)],
                              capture_output=True, text=True)

    def test_repo_epics_declare_valid_dependencies(self) -> None:
        epics = json.loads(subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py")],
                                          cwd=ROOT, check=True, capture_output=True, text=True).stdout)
        graph = {e["id"]: e["depends"] for e in epics}
        self.assertEqual(graph["E00"], [])
        self.assertEqual(graph["E01"], [])
        self.assertEqual(set(graph["E20"]), {f"E{i:02d}" for i in range(20)})

    def test_unknown_and_cyclic_dependencies_fail(self) -> None:
        self.assertIn("mövcud olmayan epic E09", self.parse({"E00": "E09"}).stderr)
        self.assertIn("dövr", self.parse({"E00": "E01", "E01": "E02", "E02": "E00"}).stderr)
        self.assertIn("özündən", self.parse({"E00": "E00"}).stderr)
        self.assertEqual(self.parse({"E00": "", "E01": "E00"}).returncode, 0)


if __name__ == "__main__":
    unittest.main()
