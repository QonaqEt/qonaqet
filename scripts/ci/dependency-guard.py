#!/usr/bin/env python3
"""QONAQET dependency-guard — lock faylları və workflow action-ları allowlist-ə qarşı (E00-T07).

    scripts/ci/dependency-guard.py            # repo kökündən; pozuntu varsa 1 ilə çıxır

Yeganə mənbə: docs/dependencies/allowlist.json. Yalnız Python stdlib — xarici asılılıq yoxdur.

Nə yoxlanılır:
  1. Birbaşa asılılıqlar allowlist-dədir:
       NuGet  packages.lock.json  → "type": "Direct"
       npm    package-lock.json   → kök və workspace paketlərinin dependencies/devDependencies/...
       pub    pubspec.lock        → "dependency: direct ..."; `source: sdk` (Flutter SDK-nın özü) platformadır
  2. `forbidden_packages` heç yerdə, keçid (transitive) asılılıqlarda da yoxdur.
  3. Workflow-larda `uses:` allowlist-dəki sahiblərdəndir və tam commit SHA ilə pinlənib.

Keçid asılılıqlarının CVE və lisenziyaları `actions/dependency-review-action` ilə yoxlanılır —
allowlist yalnız bizim seçdiyimiz paketləri sadalayır, onların öz asılılıq ağacını yox.
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from dataclasses import dataclass
from fnmatch import fnmatchcase
from pathlib import Path

ALLOWLIST = Path("docs/dependencies/allowlist.json")
LOCK_NAMES = {"packages.lock.json": "nuget", "package-lock.json": "npm", "pubspec.lock": "pub"}
SKIP_DIRS = {"node_modules", "bin", "obj", ".dart_tool", ".git", "build", "dist"}
SHA = re.compile(r"^[0-9a-f]{40}$")
USES = re.compile(r"^\s*(?:-\s*)?uses:\s*['\"]?([^'\"\s#]+)", re.MULTILINE)
NPM_DEP_FIELDS = ("dependencies", "devDependencies", "optionalDependencies", "peerDependencies")


@dataclass(frozen=True)
class Dep:
    ecosystem: str
    name: str
    direct: bool
    source: str  # fayl yolu


@dataclass(frozen=True)
class Violation:
    source: str
    ecosystem: str
    name: str
    reason: str

    def annotation(self) -> str:
        return f"::error file={self.source},title=dependency-guard::{self.ecosystem} paketi '{self.name}': {self.reason}"


# ------------------------------------------------------------ lock parserləri
def parse_nuget(path: Path) -> list[Dep]:
    data = json.loads(path.read_text(encoding="utf-8"))
    deps = []
    for framework in (data.get("dependencies") or {}).values():
        for name, info in framework.items():
            # "Project" = solution daxili layihə istinadı, paket deyil.
            if info.get("type") == "Project":
                continue
            deps.append(Dep("nuget", name, info.get("type") == "Direct", str(path)))
    return deps


def _npm_name(key: str) -> str:
    return key.rsplit("node_modules/", 1)[-1]


def parse_npm(path: Path) -> list[Dep]:
    data = json.loads(path.read_text(encoding="utf-8"))
    packages = data.get("packages")
    if packages is None:
        raise ValueError(f"{path}: lockfileVersion {data.get('lockfileVersion')} dəstəklənmir (npm 7+ lazımdır)")
    direct: set[str] = set()
    for key, info in packages.items():
        if key == "" or "node_modules/" not in key:  # kök və workspace-lər
            for field in NPM_DEP_FIELDS:
                direct.update((info.get(field) or {}).keys())
    deps = []
    for key, info in packages.items():
        if not key.startswith("node_modules/") and "/node_modules/" not in key:
            continue
        if info.get("link"):  # workspace-in özünə symlink
            continue
        name = info.get("name") or _npm_name(key)
        top_level = key.count("node_modules/") == 1
        deps.append(Dep("npm", name, top_level and name in direct, str(path)))
    return deps


def parse_pub(path: Path) -> list[Dep]:
    """pubspec.lock — sabit formatlı YAML; tam YAML parser lazım deyil."""
    deps, current, fields, in_packages = [], None, {}, False

    def flush() -> None:
        if current is not None:
            kind = fields.get("dependency", "")
            sdk = fields.get("source") == "sdk"
            deps.append(Dep("pub", current, kind.startswith("direct") and not sdk, str(path)))

    for raw in path.read_text(encoding="utf-8").splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        indent = len(raw) - len(raw.lstrip(" "))
        line = raw.strip()
        if indent == 0:
            flush()
            current, fields = None, {}
            in_packages = line == "packages:"
        elif in_packages and indent == 2 and line.endswith(":"):
            flush()
            current, fields = line[:-1].strip("\"'"), {}
        elif in_packages and indent == 4 and ":" in line:
            key, _, value = line.partition(":")
            fields[key.strip()] = value.strip().strip("\"'")
    flush()
    return deps


PARSERS = {"nuget": parse_nuget, "npm": parse_npm, "pub": parse_pub}


# ------------------------------------------------------------ yoxlama
class Allowlist:
    def __init__(self, data: dict) -> None:
        self.entries = {eco: [e["name"] for e in data.get(eco, [])] for eco in ("nuget", "npm", "pub", "github-actions")}
        self.forbidden = {e["name"].lower(): e["reason"] for e in data["policy"]["forbidden_packages"]}

    @staticmethod
    def _match(patterns: list[str], name: str, case_sensitive: bool) -> bool:
        if not case_sensitive:
            name, patterns = name.lower(), [p.lower() for p in patterns]
        return any(fnmatchcase(name, p) for p in patterns)

    def allows(self, ecosystem: str, name: str) -> bool:
        # NuGet adları registrdə hərf registrinə həssas deyil; npm və pub həssasdır.
        return self._match(self.entries[ecosystem], name, case_sensitive=ecosystem != "nuget")

    def forbidden_reason(self, name: str) -> str | None:
        lowered = name.lower()
        for forbidden, reason in self.forbidden.items():
            # "MediatR" həm "MediatR"-i, həm "MediatR.Contracts"-ı tutur; "nx" isə "nx"-i və "@nx/..."-ı.
            if lowered == forbidden or lowered.startswith(forbidden + ".") or lowered.startswith(f"@{forbidden}/"):
                return reason
        return None


def check_deps(deps: list[Dep], allow: Allowlist) -> list[Violation]:
    out = []
    for dep in deps:
        reason = allow.forbidden_reason(dep.name)
        if reason:
            kind = "birbaşa" if dep.direct else "keçid"
            out.append(Violation(dep.source, dep.ecosystem, dep.name, f"qadağandır ({kind} asılılıq): {reason}"))
        elif dep.direct and not allow.allows(dep.ecosystem, dep.name):
            out.append(
                Violation(dep.source, dep.ecosystem, dep.name,
                          "allowlist-də yoxdur — ADR + docs/dependencies/allowlist.json + 'dependency-approved' label-ı lazımdır")
            )
    return out


def check_workflows(files: list[Path], allow: Allowlist) -> list[Violation]:
    out = []
    for path in files:
        for ref in USES.findall(path.read_text(encoding="utf-8")):
            if ref.startswith("./"):
                continue
            action, _, version = ref.partition("@")
            if ref.startswith("docker://"):
                out.append(Violation(str(path), "github-actions", ref, "docker:// action-ları icazəli deyil"))
                continue
            owner_repo = "/".join(action.split("/")[:2])
            if not allow._match(allow.entries["github-actions"], owner_repo, case_sensitive=False):
                out.append(Violation(str(path), "github-actions", action, "sahibi allowlist-də yoxdur (yalnız actions/*, github/*, docker/*)"))
            if not SHA.match(version):
                out.append(Violation(str(path), "github-actions", ref, "tam commit SHA ilə pinlənməyib"))
    return out


def tracked_files(root: Path) -> list[Path]:
    try:
        out = subprocess.run(["git", "ls-files", "-z"], cwd=root, check=True, capture_output=True, text=True).stdout
        return [Path(p) for p in out.split("\0") if p]
    except (subprocess.CalledProcessError, FileNotFoundError):
        return [p.relative_to(root) for p in root.rglob("*") if p.is_file() and not SKIP_DIRS & set(p.relative_to(root).parts)]


def run(root: Path) -> tuple[list[Violation], dict[str, int]]:
    allow = Allowlist(json.loads((root / ALLOWLIST).read_text(encoding="utf-8")))
    files = tracked_files(root)
    deps: list[Dep] = []
    stats = {"lock faylı": 0, "paket": 0, "workflow": 0}
    for rel in files:
        ecosystem = LOCK_NAMES.get(rel.name)
        if ecosystem and not SKIP_DIRS & set(rel.parts[:-1]):
            found = PARSERS[ecosystem](root / rel)
            deps += [Dep(d.ecosystem, d.name, d.direct, str(rel)) for d in found]
            stats["lock faylı"] += 1
    stats["paket"] = len(deps)
    workflows = [rel for rel in files if rel.parts[:2] == (".github", "workflows") and rel.suffix in (".yml", ".yaml")]
    stats["workflow"] = len(workflows)
    violations = check_deps(deps, allow)
    violations += [Violation(v.source.replace(str(root) + "/", ""), v.ecosystem, v.name, v.reason)
                   for v in check_workflows([root / w for w in workflows], allow)]
    return violations, stats


def main(argv: list[str]) -> int:
    root = Path(argv[1]) if len(argv) > 1 else Path.cwd()
    violations, stats = run(root)
    summary = [
        "## dependency-guard",
        "",
        f"Yoxlanıldı: {stats['lock faylı']} lock faylı, {stats['paket']} paket, {stats['workflow']} workflow.",
        "",
    ]
    if violations:
        summary += ["| Fayl | Ekosistem | Paket | Səbəb |", "|---|---|---|---|"]
        summary += [f"| `{v.source}` | {v.ecosystem} | `{v.name}` | {v.reason} |" for v in violations]
        for v in violations:
            print(v.annotation())
    else:
        summary.append("✅ Pozuntu yoxdur.")
    text = "\n".join(summary) + "\n"
    print(text)
    step_summary = __import__("os").environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as fh:
            fh.write(text)
    return 1 if violations else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
