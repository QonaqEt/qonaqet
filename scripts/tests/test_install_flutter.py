#!/usr/bin/env python3
"""scripts/ci/install-flutter.sh testləri — şəbəkəsiz (mövcud lokal git repo üzərində).

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "ci" / "install-flutter.sh"


def git(*args, cwd):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


class InstallFlutterTests(unittest.TestCase):
    def test_pin_file_is_a_stable_tag_with_a_full_sha(self) -> None:
        lines = [l for l in (ROOT / "mobile/flutter-version").read_text().splitlines() if l and not l.startswith("#")]
        self.assertEqual(len(lines), 1)
        tag, sha = lines[0].split()
        self.assertRegex(tag, r"^\d+\.\d+\.\d+$", "yalnız stable x.y.z tag-ı (beta/pre yox)")
        self.assertRegex(sha, r"^[0-9a-f]{40}$")

    def test_a_checkout_at_another_commit_is_rejected(self) -> None:
        dest = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, dest)
        git("init", "-q", cwd=dest)
        git("-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false",
            "commit", "-q", "--allow-empty", "-m", "x", cwd=dest)
        env = {k: v for k, v in os.environ.items() if k != "GITHUB_PATH"}
        result = subprocess.run(["bash", str(SCRIPT), str(dest)], capture_output=True, text=True, env=env)
        self.assertEqual(result.returncode, 1)
        self.assertRegex(result.stderr, r"gözlənilən [0-9a-f]{40} deyil")

    def test_workflow_uses_the_script_and_no_third_party_flutter_action(self) -> None:
        workflow = (ROOT / ".github/workflows/ci-mobile.yml").read_text(encoding="utf-8")
        self.assertIn("scripts/ci/install-flutter.sh", workflow)
        for owner in re.findall(r"uses:\s*([\w.-]+)/", workflow):
            self.assertIn(owner, ("actions",), owner)


if __name__ == "__main__":
    unittest.main()
