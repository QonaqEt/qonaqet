#!/usr/bin/env python3
"""scripts/ci/coverage-summary.py testləri.

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "ci" / "coverage-summary.py"
spec = importlib.util.spec_from_file_location("coverage_summary", SCRIPT)
cs = importlib.util.module_from_spec(spec)
sys.modules["coverage_summary"] = cs
spec.loader.exec_module(cs)


def cobertura(packages: dict[str, dict[str, dict[int, int]]]) -> str:
    """{assembly: {fayl: {sətir: hits}}} → Cobertura XML (metod səviyyəsində təkrarlanan sətirlərlə)."""
    out = ['<?xml version="1.0"?>', '<coverage line-rate="0" version="1"><packages>']
    for package, files in packages.items():
        out.append(f'<package name="{package}"><classes>')
        for filename, lines in files.items():
            body = "".join(f'<line number="{n}" hits="{h}" branch="False"/>' for n, h in lines.items())
            out.append(
                f'<class name="C" filename="{filename}">'
                f'<methods><method name="M" signature="()"><lines>{body}</lines></method></methods>'
                f"<lines>{body}</lines></class>"
            )
        out.append("</classes></package>")
    out.append("</packages></coverage>")
    return "".join(out)


class CoverageSummaryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.addCleanup(self.tmp.cleanup)

    def write(self, relative: str, packages: dict) -> None:
        path = self.dir / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(cobertura(packages), encoding="utf-8")

    def summary(self) -> str:
        return cs.render(cs.merge(sorted(self.dir.rglob("*.cobertura.xml"))))

    def test_single_report_counts_each_line_once(self) -> None:
        self.write("a/x.cobertura.xml", {"Qonaqet.Domain": {"Gift.cs": {1: 3, 2: 0, 3: 1, 4: 0}}})
        self.assertIn("| `Qonaqet.Domain` | 2 | 4 | 50.0% |", self.summary())

    def test_reports_are_merged_per_line_across_test_projects(self) -> None:
        # Domain.Tests 1-ci və 2-ci sətri, Application.Tests 2-ci və 3-cü sətri örtür → 3/4, 2+2 yox.
        self.write("unit/a.cobertura.xml", {"Qonaqet.Domain": {"Gift.cs": {1: 1, 2: 1, 3: 0, 4: 0}}})
        self.write("app/b.cobertura.xml", {"Qonaqet.Domain": {"Gift.cs": {1: 0, 2: 5, 3: 1, 4: 0}}})
        self.assertIn("| `Qonaqet.Domain` | 3 | 4 | 75.0% |", self.summary())

    def test_test_assemblies_are_excluded_and_total_is_summed(self) -> None:
        self.write(
            "r.cobertura.xml",
            {
                "Qonaqet.Domain": {"A.cs": {1: 1, 2: 1}},
                "Qonaqet.Application": {"B.cs": {1: 0, 2: 1}},
                "Qonaqet.Domain.Tests": {"T.cs": {1: 1}},
                "Qonaqet.IntegrationTests": {"I.cs": {1: 1}},
            },
        )
        text = self.summary()
        self.assertNotIn("Tests", text)
        self.assertIn("| **Cəmi** | **3** | **4** | **75.0%** |", text)
        # əlifba sırası sabitdir
        self.assertLess(text.index("Qonaqet.Application"), text.index("Qonaqet.Domain"))

    def test_no_reports_is_stated_not_hidden(self) -> None:
        self.assertIn("Coverage hesabatı tapılmadı", self.summary())

    def test_cli_prints_markdown_and_tolerates_a_missing_directory(self) -> None:
        self.write("r.cobertura.xml", {"Qonaqet.Domain": {"A.cs": {1: 1}}})
        ok = subprocess.run([sys.executable, str(SCRIPT), str(self.dir)], capture_output=True, text=True)
        self.assertEqual(ok.returncode, 0)
        self.assertIn("100.0%", ok.stdout)
        missing = subprocess.run([sys.executable, str(SCRIPT), str(self.dir / "yoxdur")], capture_output=True, text=True)
        self.assertEqual(missing.returncode, 0)
        self.assertIn("tapılmadı", missing.stdout)


if __name__ == "__main__":
    unittest.main()
