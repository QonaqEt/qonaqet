#!/usr/bin/env python3
"""deploy/docker-compose.yml üçün statik yoxlamalar (yalnız stdlib + `docker compose config`).

    python3 -m unittest discover -s deploy/tests -v
"""
from __future__ import annotations

import json
import re
import subprocess
import unittest
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[1]
COMPOSE = DEPLOY / "docker-compose.yml"
ENV_EXAMPLE = DEPLOY / "env" / ".env.example"

INFRA = {"mongo", "postgres", "valkey", "rabbitmq", "rustfs", "rustfs-init", "mailpit", "aspire-dashboard"}
APPS = {"api", "integration-api", "worker", "landing", "business-portal", "admin"}

# Healthcheck-siz qala bilənlər: birdəfəlik init konteyneri və shell-siz (chiseled) dashboard image-i.
NO_HEALTHCHECK = {"rustfs-init", "aspire-dashboard"}

FORBIDDEN_TAG = re.compile(r"latest|preview|rc|alpha|beta|edge|nightly|unstable", re.IGNORECASE)
VAR_REF = re.compile(r"\$\{([A-Z][A-Z0-9_]*)")


def render(*profiles: str) -> dict:
    cmd = ["docker", "compose", "-f", str(COMPOSE)]
    for profile in profiles:
        cmd += ["--profile", profile]
    cmd += ["config", "--format", "json"]
    # Boş mühit: default-ların özü ilə render olunmalıdır (.env faylı olmadan).
    out = subprocess.run(cmd, check=True, capture_output=True, text=True, env={"PATH": _path()})
    return json.loads(out.stdout)


def _path() -> str:
    import os

    return os.environ.get("PATH", "/usr/bin:/bin")


def env_example_keys() -> list[str]:
    keys = []
    for line in ENV_EXAMPLE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            keys.append(line.split("=", 1)[0])
    return keys


class ComposeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.default = render()["services"]
        cls.full = render("apps")["services"]

    def test_default_profile_is_exactly_the_infrastructure(self) -> None:
        self.assertEqual(set(self.default), INFRA)

    def test_apps_profile_adds_all_hosts_and_web_apps(self) -> None:
        self.assertEqual(set(self.full), INFRA | APPS)

    def test_every_image_is_pinned_to_a_release_version(self) -> None:
        for name, svc in self.full.items():
            image = svc.get("image")
            if image is None:
                self.assertIn("build", svc, f"{name}: nə image, nə build var")
                continue
            repo, _, tag = image.rpartition(":")
            self.assertTrue(repo and tag and "/" not in tag, f"{name}: tag yoxdur → {image}")
            self.assertRegex(tag, r"\d+\.\d+", f"{name}: konkret versiya deyil → {image}")
            self.assertIsNone(FORBIDDEN_TAG.search(tag), f"{name}: qadağan olunmuş tag → {image}")

    def test_rustfs_is_pinned_to_a_ga_1_0_x_tag(self) -> None:
        self.assertRegex(self.default["rustfs"]["image"], r"^rustfs/rustfs:1\.0\.\d+$")

    def test_infrastructure_services_have_healthchecks(self) -> None:
        for name in INFRA - NO_HEALTHCHECK:
            self.assertIn("healthcheck", self.default[name], f"{name}: healthcheck yoxdur")

    def test_host_ports_bind_to_localhost_only(self) -> None:
        for name, svc in self.full.items():
            for port in svc.get("ports", []):
                self.assertEqual(port.get("host_ip"), "127.0.0.1", f"{name}: port hər interfeysə açıqdır → {port}")

    def test_mongo_runs_as_a_replica_set(self) -> None:
        self.assertIn("--replSet", self.default["mongo"]["command"])
        self.assertIn("rs.initiate", " ".join(self.default["mongo"]["healthcheck"]["test"]))

    def test_bucket_init_waits_for_rustfs_and_backends_wait_for_buckets(self) -> None:
        self.assertEqual(self.default["rustfs-init"]["depends_on"]["rustfs"]["condition"], "service_healthy")
        for name in ("api", "integration-api", "worker"):
            depends = self.full[name]["depends_on"]
            self.assertEqual(depends["rustfs-init"]["condition"], "service_completed_successfully")
            self.assertEqual(depends["mongo"]["condition"], "service_healthy")
            self.assertEqual(depends["postgres"]["condition"], "service_healthy")

    def test_bucket_init_script_never_deletes(self) -> None:
        script = (DEPLOY / "rustfs" / "init-buckets.sh").read_text(encoding="utf-8")
        for forbidden in ("delete-object", "delete-bucket", " rb ", " rm ", "Suspended"):
            self.assertNotIn(forbidden, script)
        self.assertIn("Status=Enabled", script)

    def test_env_example_documents_exactly_the_variables_compose_reads(self) -> None:
        used = set(VAR_REF.findall(COMPOSE.read_text(encoding="utf-8")))
        documented = env_example_keys()
        self.assertEqual(len(documented), len(set(documented)), ".env.example-da təkrarlanan açar var")
        self.assertEqual(used - set(documented), set(), "compose-da var, .env.example-da yoxdur")
        self.assertEqual(set(documented) - used, set(), ".env.example-da var, compose oxumur")

    def test_env_example_holds_no_real_secrets(self) -> None:
        for line in ENV_EXAMPLE.read_text(encoding="utf-8").splitlines():
            if line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            if re.search(r"(PASSWORD|SECRET|_KEY)$", key) and key != "RUSTFS_ACCESS_KEY":
                self.assertTrue(
                    value == "" or "dev-only" in value,
                    f"{key}: sirr dəyəri boş və ya açıq-aşkar 'dev-only' olmalıdır",
                )


if __name__ == "__main__":
    unittest.main()
