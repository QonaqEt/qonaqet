#!/usr/bin/env python3
"""scripts/repo-settings.py testləri — saxta GitHub API ilə (şəbəkə yoxdur).

    python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import copy
import importlib.util
import io
import json
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("repo_settings", ROOT / "scripts" / "repo-settings.py")
rs = importlib.util.module_from_spec(spec)
sys.modules["repo_settings"] = rs  # dataclass modulu sys.modules-da axtarır
spec.loader.exec_module(rs)

BASE = "repos/o/r"
PLAN = {"message": "Upgrade to GitHub Pro or make this repository public to enable this feature."}


class FakeGitHub:
    """Yaddaşda kiçik GitHub: parametrləri saxlayır, çağırışları qeyd edir."""

    def __init__(self, plan_allows: bool = True) -> None:
        self.plan_allows = plan_allows
        self.calls: list[tuple[str, str]] = []
        self.repo = {
            "allow_squash_merge": True,
            "allow_merge_commit": True,
            "allow_rebase_merge": True,
            "delete_branch_on_merge": False,
            "allow_auto_merge": False,
        }
        if plan_allows:
            self.repo["security_and_analysis"] = {f: {"status": "disabled"} for f in rs.SECURITY_FEATURES}
        self.alerts = False
        self.pvr = False
        self.actions = {"enabled": True, "allowed_actions": "all", "sha_pinning_required": False}
        self.rulesets: dict[int, dict] = {}

    def __call__(self, method: str, path: str, body=None) -> rs.Response:
        self.calls.append((method, path))
        path = path.split("?")[0]
        if path == BASE:
            if method == "PATCH":
                body = copy.deepcopy(body)
                analysis = body.pop("security_and_analysis", None)
                if analysis is not None:
                    if not self.plan_allows:
                        return rs.Response(403, PLAN)
                    self.repo["security_and_analysis"].update(analysis)
                if "allow_auto_merge" in body and not self.plan_allows:
                    body.pop("allow_auto_merge")  # GitHub Free planda bu sahəni səssizcə nəzərə almır
                self.repo.update(body)
            return rs.Response(200, copy.deepcopy(self.repo))
        if path == f"{BASE}/vulnerability-alerts":
            if method == "PUT":
                self.alerts = True
            return rs.Response(204 if self.alerts else 404, None)
        if path == f"{BASE}/private-vulnerability-reporting":
            if not self.plan_allows:
                return rs.Response(404, {"message": "Not Found"})
            if method == "PUT":
                self.pvr = True
                return rs.Response(204, None)
            return rs.Response(200, {"enabled": self.pvr})
        if path == f"{BASE}/actions/permissions":
            if method == "PUT":
                self.actions = body
                return rs.Response(204, None)
            return rs.Response(200, copy.deepcopy(self.actions))
        if path.startswith(f"{BASE}/rulesets"):
            if not self.plan_allows:
                return rs.Response(403, PLAN)
            tail = path[len(f"{BASE}/rulesets"):].strip("/")
            if method == "POST":
                new_id = len(self.rulesets) + 1
                # Server əlavə sahələr qaytarır — müqayisə bunlara dözməlidir.
                self.rulesets[new_id] = {**copy.deepcopy(body), "id": new_id, "source_type": "Repository", "node_id": "x"}
                return rs.Response(201, self.rulesets[new_id])
            if method == "PUT":
                self.rulesets[int(tail)].update(copy.deepcopy(body))
                return rs.Response(200, self.rulesets[int(tail)])
            if tail:
                return rs.Response(200, copy.deepcopy(self.rulesets[int(tail)]))
            return rs.Response(200, [{"id": i, "name": r["name"], "source_type": "Repository"} for i, r in self.rulesets.items()])
        raise AssertionError(f"gözlənilməyən çağırış: {method} {path}")

    def writes(self) -> list[tuple[str, str]]:
        return [c for c in self.calls if c[0] != "GET"]


def run(mode: str, api: FakeGitHub) -> tuple[int, str]:
    out = io.StringIO()
    with redirect_stdout(out):
        code = rs.main([mode, "--repo", "o/r"], api=api)
    return code, out.getvalue()


class RulesetDefinitionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.ruleset = json.loads(rs.RULESET_FILE.read_text(encoding="utf-8"))
        self.rules = {r["type"]: r.get("parameters", {}) for r in self.ruleset["rules"]}

    def test_targets_the_default_branch_and_is_enforced(self) -> None:
        self.assertEqual(self.ruleset["enforcement"], "active")
        self.assertEqual(self.ruleset["conditions"]["ref_name"]["include"], ["~DEFAULT_BRANCH"])

    def test_matches_claude_md_repository_settings(self) -> None:
        for required in ("pull_request", "required_linear_history", "required_signatures", "non_fast_forward"):
            self.assertIn(required, self.rules)
        pr = self.rules["pull_request"]
        self.assertEqual(pr["required_approving_review_count"], 1)
        self.assertTrue(pr["require_code_owner_review"])
        self.assertEqual(pr["allowed_merge_methods"], ["squash"])

    def test_nobody_bypasses_the_ruleset(self) -> None:
        self.assertEqual(self.ruleset["bypass_actors"], [])

    def test_required_checks_file_adds_the_status_check_rule(self) -> None:
        tmp = ROOT / "scripts" / "tests" / ".checks.tmp"
        try:
            tmp.write_text("# şərh\ncompose   # ci-infra\n\npr-checks\n", encoding="utf-8")
            self.assertEqual(rs.required_checks(tmp), ["compose", "pr-checks"])
            rule = rs.desired_ruleset(checks_file=tmp)["rules"][-1]
            self.assertEqual(rule["type"], "required_status_checks")
            self.assertEqual(rule["parameters"]["required_status_checks"], [{"context": "compose"}, {"context": "pr-checks"}])
        finally:
            tmp.unlink(missing_ok=True)


class CheckAndApplyTests(unittest.TestCase):
    def test_check_reports_differences_and_never_writes(self) -> None:
        api = FakeGitHub()
        code, out = run("--check", api)
        self.assertEqual(code, 1)
        self.assertIn("`ruleset:main` | ❌", out)
        self.assertEqual(api.writes(), [])

    def test_apply_converges_and_check_then_passes(self) -> None:
        api = FakeGitHub()
        code, _ = run("--apply", api)
        self.assertEqual(code, 0)
        self.assertFalse(api.repo["allow_merge_commit"])
        self.assertFalse(api.repo["allow_rebase_merge"])
        self.assertTrue(api.repo["delete_branch_on_merge"])
        self.assertTrue(api.actions["sha_pinning_required"])
        self.assertEqual(api.repo["security_and_analysis"]["secret_scanning_push_protection"]["status"], "enabled")
        self.assertEqual(len(api.rulesets), 1)
        self.assertEqual(run("--check", api)[0], 0)

    def test_apply_is_idempotent_for_the_ruleset(self) -> None:
        api = FakeGitHub()
        run("--apply", api)
        api.calls.clear()
        code, out = run("--apply", api)
        self.assertEqual(code, 0)
        self.assertIn("ruleset main (dəyişiklik yoxdur)", out)
        self.assertEqual([c for c in api.writes() if "rulesets" in c[1]], [])
        self.assertEqual(len(api.rulesets), 1)

    def test_apply_repairs_a_drifted_ruleset_in_place(self) -> None:
        api = FakeGitHub()
        run("--apply", api)
        api.rulesets[1]["rules"] = [r for r in api.rulesets[1]["rules"] if r["type"] != "required_signatures"]
        self.assertEqual(run("--check", api)[0], 1)
        code, out = run("--apply", api)
        self.assertEqual(code, 0)
        self.assertIn("ruleset main (yeniləndi)", out)
        self.assertEqual(len(api.rulesets), 1)

    def test_a_bypass_actor_added_by_hand_is_reported_as_drift(self) -> None:
        api = FakeGitHub()
        run("--apply", api)
        api.rulesets[1]["bypass_actors"] = [{"actor_type": "OrganizationAdmin", "bypass_mode": "always"}]
        self.assertEqual(run("--check", api)[0], 1)

    def test_plan_limits_are_reported_not_crashed_on(self) -> None:
        api = FakeGitHub(plan_allows=False)
        code, out = run("--apply", api)
        self.assertEqual(code, 1)  # tam uyğun deyil → heç vaxt yalançı "yaşıl" vermir
        self.assertIn("ruleset main: plan icazə vermir", out)
        self.assertIn("`ruleset:main` | ⛔", out)
        self.assertIn("`secret_scanning` | ⛔", out)
        # plandan asılı olmayanlar yenə də tətbiq olunub
        self.assertFalse(api.repo["allow_merge_commit"])
        self.assertTrue(api.actions["sha_pinning_required"])


if __name__ == "__main__":
    unittest.main()
