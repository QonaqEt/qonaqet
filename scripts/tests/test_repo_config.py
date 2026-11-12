#!/usr/bin/env python3
""".github/dependabot.yml və .github/CODEOWNERS üçün statik yoxlamalar (E00-T13). Yalnız stdlib.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEPENDABOT = (ROOT / ".github/dependabot.yml").read_text(encoding="utf-8")
CODEOWNERS = (ROOT / ".github/CODEOWNERS").read_text(encoding="utf-8")
BOOTSTRAP = (ROOT / "scripts/bootstrap-github.sh").read_text(encoding="utf-8")


def updates() -> list[str]:
    """`updates:` altındakı hər `- package-ecosystem:` bloku ayrıca mətn kimi."""
    body = DEPENDABOT.split("\nupdates:\n", 1)[1]
    return ["- package-ecosystem:" + b for b in body.split("- package-ecosystem:")[1:]]


def field(block: str, name: str) -> str:
    match = re.search(rf"^\s*-?\s*{name}:\s*(.+)$", block, re.MULTILINE)
    return match.group(1).strip() if match else ""


class DependabotTests(unittest.TestCase):
    def test_required_ecosystems_are_configured(self) -> None:
        ecosystems = {field(b, "package-ecosystem") for b in updates()}
        self.assertTrue({"nuget", "npm", "pub", "github-actions", "docker"} <= ecosystems, ecosystems)

    def test_every_update_is_weekly_grouped_and_conventional(self) -> None:
        for block in updates():
            name = field(block, "package-ecosystem")
            self.assertIn("interval: weekly", block, name)
            self.assertRegex(block, r"groups:\n\s+[a-z-]+:\n\s+patterns: \[\"\*\"\]", name)
            # pr-checks: başlıq `build(deps): ...` olmalıdır
            self.assertIn('prefix: "build(deps)"', block, name)

    def test_labels_exist(self) -> None:
        for block in updates():
            for label in re.findall(r"area:[a-z-]+", field(block, "labels")):
                self.assertIn(label, BOOTSTRAP, f"{label} bootstrap-da yaradılmır")

    def test_directories_are_inside_the_monorepo_layout(self) -> None:
        for block in updates():
            directory = field(block, "directory")
            top = directory.strip("/").split("/")[0]
            self.assertTrue(directory == "/" or (ROOT / top).is_dir(), directory)

    def test_infra_images_stay_on_their_claude_md_lines(self) -> None:
        compose = next(b for b in updates() if field(b, "package-ecosystem") == "docker-compose")
        for image in ("rustfs/rustfs", "mongo", "valkey/valkey", "rabbitmq"):
            self.assertRegex(compose, rf'dependency-name: "{re.escape(image)}"\n\s+update-types: \["version-update:semver-major"')
        self.assertRegex(compose, r'"rustfs/rustfs"\n\s+update-types: \[[^\]]*semver-minor')


class CodeownersTests(unittest.TestCase):
    def rules(self) -> dict[str, list[str]]:
        out = {}
        for line in CODEOWNERS.splitlines():
            if line.strip() and not line.startswith("#"):
                path, *owners = line.split()
                out[path] = owners
        return out

    def test_required_paths_are_owned(self) -> None:
        rules = self.rules()
        for path in ("/.github/", "/docs/dependencies/", "/CLAUDE.md"):
            self.assertIn(path, rules)
            self.assertTrue(rules[path], f"{path}: sahibi yoxdur")

    def test_owners_are_the_owners_team(self) -> None:
        for path, owners in self.rules().items():
            self.assertEqual(owners, ["@QonaqEt/owners"], path)

    def test_owned_paths_exist(self) -> None:
        for path in self.rules():
            self.assertTrue((ROOT / path.strip("/")).exists(), f"{path} repoda yoxdur")


if __name__ == "__main__":
    unittest.main()
