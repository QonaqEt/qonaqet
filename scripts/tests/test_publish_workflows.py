#!/usr/bin/env python3
"""docker-publish.yml və release.yml üçün statik yoxlamalar (E00-T12). Yalnız stdlib + `docker compose config`.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import json
import re
import shutil
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLISH = (ROOT / ".github/workflows/docker-publish.yml").read_text(encoding="utf-8")
RELEASE = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
MATRIX = re.findall(r"- \{ image: ([\w-]+),\s+dockerfile: ([\w/.-]+),\s+arg: \"(\w+)=([\w.-]+)\"(?:, target: ([\w-]+))? \}", PUBLISH)


class DockerPublishTests(unittest.TestCase):
    def test_sbom_and_provenance_are_produced(self) -> None:
        self.assertIn("sbom: true", PUBLISH)
        self.assertIn("provenance: mode=max", PUBLISH)
        self.assertIn("actions/attest-build-provenance@", PUBLISH)
        for permission in ("packages: write", "id-token: write", "attestations: write"):
            self.assertIn(permission, PUBLISH)

    def test_runs_on_push_to_main_only(self) -> None:
        self.assertRegex(PUBLISH, r"on:\n  push:\n    branches: \[main\]")
        self.assertNotIn("pull_request", PUBLISH)

    @unittest.skipUnless(shutil.which("docker"), "docker yoxdur")
    def test_matrix_matches_the_compose_apps(self) -> None:
        cfg = json.loads(subprocess.run(
            ["docker", "compose", "-f", str(ROOT / "deploy/docker-compose.yml"), "--profile", "apps", "config", "--format", "json"],
            check=True, capture_output=True, text=True).stdout)["services"]
        built = {name: svc["build"] for name, svc in cfg.items() if "build" in svc}
        self.assertEqual({m[0] for m in MATRIX}, set(built))
        for image, dockerfile, key, value, target in MATRIX:
            self.assertTrue(built[image]["dockerfile"].endswith(dockerfile), image)
            self.assertEqual(built[image]["args"][key], value, image)
            # Worker Chromium-lu ayrıca stage-dən qurulur; publish və compose eyni target-i işlətməlidir.
            self.assertEqual(built[image].get("target", ""), target, image)


class ReleaseTests(unittest.TestCase):
    def test_release_on_version_tags_with_generated_notes(self) -> None:
        self.assertRegex(RELEASE, r'tags: \["v\*"\]')
        self.assertIn("--generate-notes", RELEASE)
        self.assertIn("--verify-tag", RELEASE)
        self.assertIn("contents: write", RELEASE)

    def test_release_categories_use_existing_area_labels(self) -> None:
        config = (ROOT / ".github/release.yml").read_text(encoding="utf-8")
        labeler = (ROOT / ".github/labeler.yml").read_text(encoding="utf-8")
        for label in set(re.findall(r"area:[a-z-]+", config)):
            self.assertIn(f"{label}:", labeler, label)


if __name__ == "__main__":
    unittest.main()
