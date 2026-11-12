#!/usr/bin/env python3
"""Cobertura hesabatlarından Markdown coverage xülasəsi (ci-backend job summary üçün).

    scripts/ci/coverage-summary.py backend/TestResults >> "$GITHUB_STEP_SUMMARY"

Öz kodumuzdur — ReportGenerator kimi xarici alət işlədilmir (CLAUDE.md → "Dependency policy").

Bir neçə test layihəsi eyni assembly-ni örtə bilər, ona görə hesabatlar sətir səviyyəsində
birləşdirilir: sətir hər hansı hesabatda icra olunubsa, örtülmüş sayılır. Test assembly-ləri
(`*.Tests`, `*Tests`) cədvələ düşmür.
"""
from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

Lines = dict[tuple[str, int], bool]  # (fayl, sətir) → örtülüb?


def is_test_assembly(name: str) -> bool:
    return name.endswith("Tests")


def merge(reports: list[Path]) -> dict[str, Lines]:
    packages: dict[str, Lines] = {}
    for report in reports:
        root = ET.parse(report).getroot()
        for package in root.iter("package"):
            name = package.get("name", "?")
            if is_test_assembly(name):
                continue
            lines = packages.setdefault(name, {})
            for cls in package.iter("class"):
                filename = cls.get("filename", "?")
                # Yalnız sinfin birbaşa <lines> bloku: <methods> altındakılar eyni sətirlərin təkrarıdır.
                for line in cls.findall("./lines/line"):
                    key = (filename, int(line.get("number", "0")))
                    lines[key] = lines.get(key, False) or int(line.get("hits", "0")) > 0
    return packages


def percent(covered: int, total: int) -> str:
    return "—" if total == 0 else f"{covered * 100 / total:.1f}%"


def render(packages: dict[str, Lines]) -> str:
    out = ["## Coverage", ""]
    if not packages:
        return "\n".join(out + ["Coverage hesabatı tapılmadı (`*.cobertura.xml`)."]) + "\n"
    out += ["| Assembly | Örtülmüş sətir | Cəmi | Coverage |", "|---|---:|---:|---:|"]
    all_covered = all_total = 0
    for name in sorted(packages):
        covered = sum(packages[name].values())
        total = len(packages[name])
        all_covered += covered
        all_total += total
        out.append(f"| `{name}` | {covered} | {total} | {percent(covered, total)} |")
    out.append(f"| **Cəmi** | **{all_covered}** | **{all_total}** | **{percent(all_covered, all_total)}** |")
    return "\n".join(out) + "\n"


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    reports = sorted(Path(argv[1]).rglob("*.cobertura.xml")) if Path(argv[1]).is_dir() else []
    sys.stdout.write(render(merge(reports)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
