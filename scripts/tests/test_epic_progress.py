#!/usr/bin/env python3
"""scripts/ci/epic-progress.py testləri — saxta GitHub ilə.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("epic_progress", ROOT / "scripts" / "ci" / "epic-progress.py")
ep = importlib.util.module_from_spec(spec)
sys.modules["epic_progress"] = ep
spec.loader.exec_module(ep)


class FakeGitHub:
    def __init__(self, tags=()) -> None:
        self.tags = dict.fromkeys(tags, "old")
        self.writes = []

    def __call__(self, method, path, body):
        if path.startswith("git/ref/tags/"):
            return (200, {}) if path.removeprefix("git/ref/tags/") in self.tags else (404, {})
        if path == "git/refs" and method == "POST":
            self.writes.append(body["ref"])
            self.tags[body["ref"].removeprefix("refs/tags/")] = body["sha"]
            return 201, {}
        raise AssertionError(path)


class EpicProgressTests(unittest.TestCase):
    def test_missing_tags_are_created_on_the_given_commit(self) -> None:
        gh = FakeGitHub()
        self.assertEqual(ep.run(gh, ["E00", "E01"], "a" * 40, log=lambda _: None), ["epic-E00-done", "epic-E01-done"])
        self.assertEqual(gh.tags["epic-E00-done"], "a" * 40)

    def test_existing_tags_are_left_alone(self) -> None:
        gh = FakeGitHub(tags=["epic-E00-done"])
        self.assertEqual(ep.run(gh, ["E00"], "b" * 40, log=lambda _: None), [])
        self.assertEqual(gh.writes, [])
        self.assertEqual(gh.tags["epic-E00-done"], "old")

    def test_done_epics_are_read_from_markdown(self) -> None:
        done = ep.done_epics()
        self.assertIsInstance(done, list)
        self.assertTrue(all(e.startswith("E") for e in done))


if __name__ == "__main__":
    unittest.main()
