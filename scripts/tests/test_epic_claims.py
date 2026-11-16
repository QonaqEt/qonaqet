#!/usr/bin/env python3
"""scripts/epic.py, scripts/board.py və epic `depends:`/`status:` testləri — müvəqqəti md faylları ilə.

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


def epic_md(eid, depends="", owner="", status="todo", tasks=(("T01", "todo", ""),)):
    out = [f"---\nid: {eid}\ntitle: Epic {eid}\narea: infra\npriority: p0\ndepends: {depends}\nowner: {owner}\n"
           f"status: {status}\n---\n\n## Goal\nx\n\n## Tasks\n"]
    for num, st, labels in tasks:
        extra = f"- labels: {labels}\n" if labels else ""
        out.append(f"\n### {eid}-{num} · Task {num}\n- area: infra\n- priority: p0\n- size: S\n{extra}- status: {st}\n\n"
                   f"Context: x\n\nAcceptance:\n- [ ] y\n")
    return "".join(out)


class Repo:
    def __init__(self, files: dict[str, str]) -> None:
        self.root = Path(tempfile.mkdtemp())
        (self.root / "docs/epics").mkdir(parents=True)
        for eid, text in files.items():
            (self.root / f"docs/epics/{eid}.md").write_text(text, encoding="utf-8")
        self.epics = ec.Epics(self.root)

    def claims(self, user="zahid"):
        return ec.Claims(self.epics, user, log=lambda _: None)

    def load(self):
        return self.epics.load()


class ClaimTests(unittest.TestCase):
    def setUp(self) -> None:
        self.repo = Repo({
            "E00": epic_md("E00", owner="ana", status="done", tasks=(("T01", "done", ""),)),
            "E01": epic_md("E01", tasks=(("T01", "todo", ""), ("T02", "todo", "parallel-ok"))),
            "E02": epic_md("E02", depends="E01"),
            "E03": epic_md("E03", depends="E00"),
        })
        self.addCleanup(shutil.rmtree, self.repo.root)

    def test_claim_writes_owner_and_status_and_the_board(self) -> None:
        self.repo.claims().claim("E01")
        e = self.repo.load()["E01"]
        self.assertEqual((e["owner"], e["status"]), ("zahid", "in-progress"))
        board = (self.repo.root / "docs/BOARD.md").read_text(encoding="utf-8")
        self.assertIn("| [E01](epics/E01.md) | Epic E01 | 🔄 in-progress | @zahid | 0/2 | — |", board)

    def test_claim_is_idempotent_for_the_owner(self) -> None:
        self.repo.claims().claim("E01")
        before = self.repo.epics.path("E01").read_text(encoding="utf-8")
        self.repo.claims().claim("E01")
        self.assertEqual(self.repo.epics.path("E01").read_text(encoding="utf-8"), before)

    def test_epic_held_by_someone_else_cannot_be_claimed(self) -> None:
        self.repo.claims("ali").claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "@ali tərəfindən işlənir"):
            self.repo.claims().claim("E01")

    def test_one_open_epic_per_developer(self) -> None:
        self.repo.claims().claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "səndə artıq E01 var"):
            self.repo.claims().claim("E03")

    def test_dependencies_must_be_done(self) -> None:
        with self.assertRaisesRegex(ec.ClaimError, "E02 hələ E01"):
            self.repo.claims().claim("E02")
        with self.assertRaisesRegex(ec.ClaimError, "E00 artıq bitib"):
            self.repo.claims().claim("E00")
        self.repo.claims().claim("E03")  # E00 done → açıqdır

    def test_release(self) -> None:
        self.repo.claims().claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "səndə deyil"):
            self.repo.claims("ali").release("E01")
        self.repo.claims().release("E01")
        e = self.repo.load()["E01"]
        self.assertEqual((e["owner"], e["status"]), ("", "todo"))

    def test_task_status_requires_owning_the_epic_unless_parallel_ok(self) -> None:
        with self.assertRaisesRegex(ec.ClaimError, "claim E01"):
            self.repo.claims().task("E01-T01", "in-progress")
        self.repo.claims("ali").claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "@ali məxsusdur"):
            self.repo.claims().task("E01-T01", "in-progress")
        self.repo.claims().task("E01-T02", "in-progress")  # parallel-ok
        self.assertEqual(self.repo.load()["E01"]["tasks"][1]["status"], "in-progress")

    def test_last_task_done_marks_the_epic_done(self) -> None:
        claims = self.repo.claims()
        claims.claim("E01")
        claims.task("E01-T01", "done")
        self.assertEqual(self.repo.load()["E01"]["status"], "in-progress")
        claims.task("E01-T02", "dropped")
        e = self.repo.load()["E01"]
        self.assertEqual((e["status"], e["owner"]), ("done", "zahid"))
        # E01 bitdi → E02 açıldı və zahid-də açıq epic qalmadı
        claims.claim("E02")

    def test_unknown_task_and_status(self) -> None:
        self.repo.claims().claim("E01")
        with self.assertRaisesRegex(ec.ClaimError, "tapılmadı"):
            self.repo.claims().task("E01-T09", "done")
        with self.assertRaisesRegex(ec.ClaimError, "status yalnız"):
            self.repo.claims().task("E01-T01", "finished")

    def test_next_and_status(self) -> None:
        self.assertEqual(self.repo.claims().next()["id"], "E01")
        self.repo.claims("ali").claim("E01")
        self.assertEqual(self.repo.claims().next()["id"], "E03")
        text = self.repo.claims().status()
        self.assertIn("E01   0/2   🔒 @ali", text)
        self.assertIn("E02   0/1   ⏳ gözləyir: E01", text)
        self.assertIn("E00   1/1   ✅ bağlı", text)


class ParsingAndBoardTests(unittest.TestCase):
    def parse(self, files: dict[str, str]):
        tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, tmp)
        for name, text in files.items():
            (tmp / f"{name}.md").write_text(text, encoding="utf-8")
        return subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py"), str(tmp)],
                              capture_output=True, text=True)

    def test_repo_epics_parse_with_status_and_dependencies(self) -> None:
        epics = json.loads(subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py")],
                                          cwd=ROOT, check=True, capture_output=True, text=True).stdout)
        graph = {e["id"]: e["depends"] for e in epics}
        self.assertEqual(graph["E01"], [])
        self.assertEqual(set(graph["E20"]), {f"E{i:02d}" for i in range(20)})
        for e in epics:
            self.assertIn(e["status"], ("todo", "in-progress", "done"))
            for t in e["tasks"]:
                self.assertIn(t["status"], ("todo", "in-progress", "done", "dropped"), t["key"])

    def test_invalid_dependencies_and_statuses_fail(self) -> None:
        self.assertIn("mövcud olmayan epic E09", self.parse({"E00": epic_md("E00", depends="E09")}).stderr)
        self.assertIn("dövr", self.parse({"E00": epic_md("E00", depends="E01"), "E01": epic_md("E01", depends="E00")}).stderr)
        self.assertIn("naməlum status", self.parse({"E00": epic_md("E00", tasks=(("T01", "finished", ""),))}).stderr)
        self.assertIn("owner yoxdur", self.parse({"E00": epic_md("E00", status="in-progress")}).stderr)
        self.assertEqual(self.parse({"E00": epic_md("E00"), "E01": epic_md("E01", depends="E00")}).returncode, 0)

    def test_legacy_done_flag_still_maps_to_done(self) -> None:
        text = epic_md("E00").replace("- status: todo\n", "- done: true\n")
        out = json.loads(self.parse({"E00": text}).stdout)
        self.assertEqual(out[0]["tasks"][0]["status"], "done")

    def test_committed_board_is_up_to_date(self) -> None:
        result = subprocess.run([sys.executable, str(ROOT / "scripts/board.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout)


if __name__ == "__main__":
    unittest.main()
