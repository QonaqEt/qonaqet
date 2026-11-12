#!/usr/bin/env python3
"""QONAQET — repo parametrləri və `main` ruleset-i (CLAUDE.md → "Repository settings", E00-T03).

    scripts/repo-settings.py --check    # heç nə dəyişmir; fərq varsa 1 ilə çıxır
    scripts/repo-settings.py --apply    # çatışmayanları tətbiq edir, sonra yenidən yoxlayır

Yalnız GitHub REST işlədir (`gh api`), GraphQL yox. İdempotentdir. Repo admin icazəsi tələb edir
(GITHUB_TOKEN kifayət deyil — admin istifadəçinin `repo` scope-lu token-i lazımdır).

Bəzi parametrlər GitHub planından asılıdır: Free planda **private** repo üçün ruleset, branch
protection, CODEOWNERS məcburiyyəti, auto-merge və secret scanning mövcud deyil. Belə hallarda skript
yıxılmır — parametri "plan icazə vermir" kimi göstərir və 1 ilə çıxır.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

ROOT = Path(__file__).resolve().parents[1]
RULESET_FILE = ROOT / ".github" / "rulesets" / "main.json"
CHECKS_FILE = ROOT / ".github" / "rulesets" / "required-checks.txt"

OK, DIFF, UNAVAILABLE = "ok", "fərqli", "plan icazə vermir"

REPO_FLAGS = {
    "allow_squash_merge": True,
    "allow_merge_commit": False,
    "allow_rebase_merge": False,
    "delete_branch_on_merge": True,
    "allow_auto_merge": True,
}
SECURITY_FEATURES = ("secret_scanning", "secret_scanning_push_protection")


@dataclass
class Response:
    status: int
    body: Any

    @property
    def plan_blocked(self) -> bool:
        message = self.body.get("message", "") if isinstance(self.body, dict) else ""
        return self.status in (402, 403) and ("Upgrade to" in message or "not available" in message.lower())


# (method, path, body) -> Response. Testlərdə saxta ilə əvəz olunur.
Api = Callable[[str, str, Any], Response]


def gh_api(method: str, path: str, body: Any = None) -> Response:
    cmd = ["gh", "api", "-i", "-X", method, path, "-H", "X-GitHub-Api-Version: 2022-11-28"]
    stdin = None
    if body is not None:
        cmd += ["--input", "-"]
        stdin = json.dumps(body)
    proc = subprocess.run(cmd, input=stdin, capture_output=True, text=True)
    head, _, payload = proc.stdout.replace("\r\n", "\n").partition("\n\n")
    try:
        status = int(head.split()[1])
    except (IndexError, ValueError):
        raise SystemExit(f"gh api cavabı oxunmadı ({method} {path}): {proc.stderr.strip() or proc.stdout[:200]}")
    try:
        parsed = json.loads(payload) if payload.strip() else None
    except json.JSONDecodeError:
        parsed = {"message": payload.strip()}
    return Response(status, parsed)


def required_checks(path: Path = CHECKS_FILE) -> list[str]:
    checks = []
    for line in path.read_text(encoding="utf-8").splitlines():
        name = line.split("#", 1)[0].strip()
        if name:
            checks.append(name)
    return checks


def desired_ruleset(ruleset_file: Path = RULESET_FILE, checks_file: Path = CHECKS_FILE) -> dict:
    ruleset = json.loads(ruleset_file.read_text(encoding="utf-8"))
    checks = required_checks(checks_file)
    if checks:
        ruleset["rules"].append(
            {
                "type": "required_status_checks",
                "parameters": {
                    "strict_required_status_checks_policy": False,
                    "required_status_checks": [{"context": c} for c in checks],
                },
            }
        )
    return ruleset


def _subset(want: Any, have: Any) -> bool:
    """`want`-dakı hər şey `have`-də eyni dəyərlə var (server əlavə sahələr qaytara bilər)."""
    if isinstance(want, dict):
        return isinstance(have, dict) and all(k in have and _subset(v, have[k]) for k, v in want.items())
    if isinstance(want, list):
        return isinstance(have, list) and len(want) == len(have) and all(_subset(w, h) for w, h in zip(want, have))
    return want == have


def ruleset_matches(want: dict, have: dict) -> bool:
    def rules(rs: dict) -> dict:
        return {r["type"]: r.get("parameters", {}) for r in rs.get("rules", [])}

    scalar = all(have.get(k) == want[k] for k in ("name", "target", "enforcement"))
    want_rules, have_rules = rules(want), rules(have)
    return (
        scalar
        and _subset(want["conditions"], have.get("conditions"))
        and (have.get("bypass_actors") or []) == want["bypass_actors"]
        and set(want_rules) == set(have_rules)
        and all(_subset(want_rules[t], have_rules[t]) for t in want_rules)
    )


class Settings:
    def __init__(self, repo: str, api: Api = gh_api) -> None:
        self.repo = repo
        self.api = api
        self.base = f"repos/{repo}"

    # ------------------------------------------------------------ check
    def check(self) -> list[tuple[str, str, str]]:
        """[(parametr, vəziyyət, izah)]"""
        rows: list[tuple[str, str, str]] = []
        repo = self.api("GET", self.base, None)
        if repo.status != 200:
            raise SystemExit(f"repo oxunmadı: HTTP {repo.status} {repo.body}")

        for flag, want in REPO_FLAGS.items():
            have = repo.body.get(flag)
            rows.append((flag, OK if have == want else DIFF, f"gözlənilən {want}, hazırda {have}"))

        analysis = repo.body.get("security_and_analysis") or {}
        for feature in SECURITY_FEATURES:
            if feature not in analysis:
                rows.append((feature, UNAVAILABLE, "security_and_analysis bu repo üçün qaytarılmır"))
            else:
                status = analysis[feature].get("status")
                rows.append((feature, OK if status == "enabled" else DIFF, f"hazırda {status}"))

        rows.append(self._toggle("dependabot_alerts", f"{self.base}/vulnerability-alerts", on_status=204))
        pvr = self.api("GET", f"{self.base}/private-vulnerability-reporting", None)
        if pvr.status == 200:
            enabled = bool(pvr.body.get("enabled"))
            rows.append(("private_vulnerability_reporting", OK if enabled else DIFF, f"hazırda {enabled}"))
        else:
            rows.append(("private_vulnerability_reporting", UNAVAILABLE, f"HTTP {pvr.status}"))

        actions = self.api("GET", f"{self.base}/actions/permissions", None)
        if actions.status == 200:
            pinned = bool(actions.body.get("sha_pinning_required"))
            rows.append(("actions_sha_pinning_required", OK if pinned else DIFF, f"hazırda {pinned}"))
        else:
            rows.append(("actions_sha_pinning_required", DIFF, f"oxunmadı: HTTP {actions.status}"))

        rows.append(self._check_ruleset())
        return rows

    def _toggle(self, name: str, path: str, on_status: int) -> tuple[str, str, str]:
        r = self.api("GET", path, None)
        if r.status == on_status:
            return (name, OK, "aktiv")
        if r.status == 404:
            return (name, DIFF, "söndürülüb")
        return (name, UNAVAILABLE if r.plan_blocked else DIFF, f"HTTP {r.status}")

    def _find_ruleset(self, name: str) -> tuple[Response, dict | None]:
        listing = self.api("GET", f"{self.base}/rulesets?per_page=100", None)
        if listing.status != 200:
            return listing, None
        for item in listing.body:
            if item.get("name") == name and item.get("source_type", "Repository") == "Repository":
                detail = self.api("GET", f"{self.base}/rulesets/{item['id']}", None)
                return detail, detail.body if detail.status == 200 else None
        return listing, None

    def _check_ruleset(self) -> tuple[str, str, str]:
        want = desired_ruleset()
        response, have = self._find_ruleset(want["name"])
        if response.plan_blocked:
            return ("ruleset:main", UNAVAILABLE, "Free planda private repo üçün ruleset yoxdur")
        if response.status != 200:
            return ("ruleset:main", DIFF, f"oxunmadı: HTTP {response.status}")
        if have is None:
            return ("ruleset:main", DIFF, "yaradılmayıb")
        return ("ruleset:main", OK if ruleset_matches(want, have) else DIFF, "tərif .github/rulesets/main.json")

    # ------------------------------------------------------------ apply
    def apply(self) -> list[str]:
        log: list[str] = []

        def call(label: str, method: str, path: str, body: Any = None) -> Response:
            r = self.api(method, path, body)
            if r.status < 300:
                log.append(f"✓ {label}")
            elif r.plan_blocked:
                log.append(f"– {label}: plan icazə vermir")
            else:
                message = r.body.get("message") if isinstance(r.body, dict) else r.body
                log.append(f"✗ {label}: HTTP {r.status} {message}")
            return r

        call("merge parametrləri", "PATCH", self.base, dict(REPO_FLAGS))
        call(
            "secret scanning + push protection",
            "PATCH",
            self.base,
            {"security_and_analysis": {f: {"status": "enabled"} for f in SECURITY_FEATURES}},
        )
        call("Dependabot alerts", "PUT", f"{self.base}/vulnerability-alerts")
        call("private vulnerability reporting", "PUT", f"{self.base}/private-vulnerability-reporting")

        actions = self.api("GET", f"{self.base}/actions/permissions", None)
        if actions.status == 200:
            body = {
                "enabled": actions.body.get("enabled", True),
                "allowed_actions": actions.body.get("allowed_actions", "all"),
                "sha_pinning_required": True,
            }
            call("Actions: SHA pin tələbi", "PUT", f"{self.base}/actions/permissions", body)
        else:
            log.append(f"✗ Actions: SHA pin tələbi: parametrlər oxunmadı (HTTP {actions.status})")

        want = desired_ruleset()
        response, have = self._find_ruleset(want["name"])
        if response.plan_blocked:
            log.append("– ruleset main: plan icazə vermir")
        elif have is None:
            call("ruleset main (yaradıldı)", "POST", f"{self.base}/rulesets", want)
        elif ruleset_matches(want, have):
            log.append("✓ ruleset main (dəyişiklik yoxdur)")
        else:
            call("ruleset main (yeniləndi)", "PUT", f"{self.base}/rulesets/{have['id']}", want)
        return log


def render(rows: list[tuple[str, str, str]]) -> str:
    icon = {OK: "✅", DIFF: "❌", UNAVAILABLE: "⛔"}
    lines = ["| Parametr | Vəziyyət | Qeyd |", "|---|---|---|"]
    lines += [f"| `{name}` | {icon[state]} {state} | {note} |" for name, state, note in rows]
    return "\n".join(lines)


def main(argv: list[str] | None = None, api: Api = gh_api) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--apply", action="store_true")
    parser.add_argument("--repo", default=os.environ.get("REPO", "QonaqEt/qonaqet"))
    args = parser.parse_args(argv)

    settings = Settings(args.repo, api)
    out = []
    if args.apply:
        out += ["### Tətbiq", ""] + [f"- {line}" for line in settings.apply()] + [""]
    rows = settings.check()
    out += ["### Vəziyyət", "", render(rows)]
    text = "\n".join(out)
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write(f"## Repo parametrləri ({args.repo})\n\n{text}\n")
    return 0 if all(state == OK for _, state, _ in rows) else 1


if __name__ == "__main__":
    sys.exit(main())
