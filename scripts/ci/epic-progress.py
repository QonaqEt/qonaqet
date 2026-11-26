#!/usr/bin/env python3
"""QONAQET epic-progress — `status: done` olan epic-lərə `epic-Exx-done` tag-ı qoyur (ADR 0004).

    epic-progress.py            # main-ə push-dan sonra: docs/epics/*.md oxunur, çatışmayan tag-lar yaradılır

Mühit: GITHUB_TOKEN, REPO, GITHUB_SHA (tag-ın qoyulacağı commit). Yalnız REST və Python stdlib.
İdempotentdir: mövcud tag-a toxunulmur. Epic-in `done` olmasını `scripts/epic.py task ... done`
(sonuncu task bitəndə) qeyd edir; o dəyişiklik main-ə düşən commit tag alır.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Callable

ROOT = Path(__file__).resolve().parents[2]

# (method, path, body) → (status, json)
Api = Callable[[str, str, Any], "tuple[int, Any]"]


def github_api(repo: str, token: str) -> Api:
    def call(method: str, path: str, body: Any = None) -> tuple[int, Any]:
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(f"https://api.github.com/repos/{repo}/{path}", data=data, method=method, headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Content-Type": "application/json",
        })
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                raw = resp.read()
                return resp.status, json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            return error.code, {"message": error.read().decode(errors="replace")[:300]}

    return call


def done_epics(epics_dir: Path = ROOT / "docs" / "epics") -> list[str]:
    out = subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py"), str(epics_dir)],
                         check=True, capture_output=True, encoding="utf-8").stdout
    return [e["id"] for e in json.loads(out) if e["status"] == "done"]


def run(api: Api, epic_ids: list[str], sha: str, log: Callable[[str], None] = print) -> list[str]:
    created = []
    for epic_id in epic_ids:
        tag = f"epic-{epic_id}-done"
        status, _ = api("GET", f"git/ref/tags/{tag}", None)
        if status == 200:
            log(f"{tag} artıq var")
            continue
        if status != 404:
            raise SystemExit(f"{tag} yoxlanmadı: HTTP {status}")
        status, body = api("POST", "git/refs", {"ref": f"refs/tags/{tag}", "sha": sha})
        if status >= 300:
            raise SystemExit(f"{tag} yaradılmadı: HTTP {status} {body}")
        log(f"{tag} → {sha[:7]}")
        created.append(tag)
    return created


def main() -> int:
    api = github_api(os.environ["REPO"], os.environ["GITHUB_TOKEN"])
    created = run(api, done_epics(), os.environ["GITHUB_SHA"])
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write("## epic-progress\n\n" + ("\n".join(f"- 🏷️ `{t}`" for t in created) or "Yeni tag yoxdur.") + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
