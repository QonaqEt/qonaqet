#!/usr/bin/env python3
"""QONAQET — epic və task vəziyyəti lokal markdown-da (ADR 0004).

    scripts/epic.py status                        # epic-lər: kim işləyir, nə götürülə bilər
    scripts/epic.py next                          # götürülə bilən ilk epic
    scripts/epic.py claim E05                     # epic-i özünə götür
    scripts/epic.py release E05                   # epic-i burax
    scripts/epic.py task E05-T03 in-progress      # task statusu: todo | in-progress | done | dropped

Mənbə `docs/epics/Exx.md`-dir: epic front matter-də `owner` və `status`, hər task-da `- status:`.
Skript yalnız lokal faylları dəyişir və `docs/BOARD.md`-ni yenidən yaradır — dəyişikliyi öz PR-ında
commit et. `pr-checks` tətbiq edir: PR-ın task-ı başqasının epic-inə aiddirsə, PR qırılır.

Qaydalar (CLAUDE.md → "How Claude Code works"):
  - bir developer eyni anda yalnız bir açıq epic saxlayır
  - epic yalnız `depends:`-dəki epic-lərin hamısı `done` olanda götürülə bilər
  - `parallel-ok` task-ı epic sahibi olmayan developer də götürə bilər
  - epic-in bütün task-ları `done`/`dropped` olanda epic `done` olur (owner tarixçə üçün qalır)

İstifadəçi (GitHub login): `--user`, `QONAQET_DEV`, `git config github.user`, sonra `gh api user`.
Yalnız Python stdlib.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STATUSES = ("todo", "in-progress", "done", "dropped")


class ClaimError(Exception):
    pass


class Epics:
    def __init__(self, root: Path = ROOT) -> None:
        self.root = root
        self.dir = root / "docs" / "epics"

    # ---------------------------------------------------------- oxuma
    def load(self) -> dict[str, dict]:
        out = subprocess.run([sys.executable, str(ROOT / "scripts/lib/parse-epics.py"), str(self.dir)],
                             check=True, capture_output=True, text=True).stdout
        return {e["id"]: e for e in json.loads(out)}

    def path(self, epic_id: str) -> Path:
        return self.dir / f"{epic_id}.md"

    # ---------------------------------------------------------- yazma
    def _set_front(self, epic_id: str, **values: str) -> None:
        path = self.path(epic_id)
        text = path.read_text(encoding="utf-8")
        end = text.index("\n---\n", 4)
        front, rest = text[:end], text[end:]
        for key, value in values.items():
            line = f"{key}: {value}".rstrip()
            if re.search(rf"^{key}:.*$", front, flags=re.M):
                front = re.sub(rf"^{key}:.*$", line, front, count=1, flags=re.M)
            else:
                front += "\n" + line
        path.write_text(front + rest, encoding="utf-8")

    def _set_task(self, key: str, status: str) -> None:
        path = self.path(key.split("-")[0])
        text = path.read_text(encoding="utf-8")
        head = re.search(rf"^### {re.escape(key)} · .*$", text, flags=re.M)
        if not head:
            raise ClaimError(f"{key} tapılmadı")
        nxt = re.search(r"^### ", text[head.end():], flags=re.M)
        end = head.end() + (nxt.start() if nxt else len(text) - head.end())
        block = text[head.end():end]
        if re.search(r"^- status:.*$", block, flags=re.M):
            block = re.sub(r"^- status:.*$", f"- status: {status}", block, count=1, flags=re.M)
        else:
            block = re.sub(r"^(- size:.*)$", rf"\1\n- status: {status}", block, count=1, flags=re.M)
        path.write_text(text[:head.end()] + block + text[end:], encoding="utf-8")

    def regenerate_board(self) -> None:
        subprocess.run([sys.executable, str(ROOT / "scripts/board.py"), "--root", str(self.root)],
                       check=True, capture_output=True)


class Claims:
    def __init__(self, epics: Epics, user: str, log=print) -> None:
        self.epics, self.user, self.log = epics, user.lstrip("@"), log

    @staticmethod
    def blockers(epic: dict, all_epics: dict[str, dict]) -> list[str]:
        return [d for d in epic["depends"] if all_epics[d]["status"] != "done"]

    def held(self, all_epics: dict[str, dict]) -> list[str]:
        return [e["id"] for e in all_epics.values() if e["owner"] == self.user and e["status"] != "done"]

    def status(self) -> str:
        all_epics = self.epics.load()
        rows = []
        for e in all_epics.values():
            live = [t for t in e["tasks"] if t["status"] != "dropped"]
            done = sum(t["status"] == "done" for t in live)
            blockers = self.blockers(e, all_epics)
            if e["status"] == "done":
                state = "✅ bağlı"
            elif e["owner"]:
                state = f"🔒 @{e['owner']}"
            elif blockers:
                state = f"⏳ gözləyir: {', '.join(blockers)}"
            else:
                state = "🟢 götürülə bilər"
            rows.append(f"{e['id']}  {done:>2}/{len(live):<2}  {state}  — {e['title']}")
        return "\n".join(rows)

    def next(self) -> dict | None:
        all_epics = self.epics.load()
        for e in all_epics.values():
            if e["status"] == "todo" and not e["owner"] and not self.blockers(e, all_epics):
                return e
        return None

    def claim(self, epic_id: str) -> None:
        all_epics = self.epics.load()
        if epic_id not in all_epics:
            raise ClaimError(f"{epic_id} tapılmadı")
        epic = all_epics[epic_id]
        if epic["status"] == "done":
            raise ClaimError(f"{epic_id} artıq bitib")
        if epic["owner"] and epic["owner"] != self.user:
            raise ClaimError(f"{epic_id} artıq @{epic['owner']} tərəfindən işlənir — `scripts/epic.py next` ilə başqasını seç")
        if epic["owner"] == self.user:
            self.log(f"{epic_id} artıq səndədir")
            return
        held = [e for e in self.held(all_epics) if e != epic_id]
        if held:
            raise ClaimError(f"səndə artıq {', '.join(held)} var — əvvəlcə onu bitir və ya `release` et")
        blockers = self.blockers(epic, all_epics)
        if blockers:
            raise ClaimError(f"{epic_id} hələ {', '.join(blockers)} epic-lərini gözləyir")
        self.epics._set_front(epic_id, owner=self.user, status="in-progress")
        self.epics.regenerate_board()
        self.log(f"{epic_id} götürüldü → @{self.user}. Dəyişikliyi PR-ına daxil et.")

    def release(self, epic_id: str) -> None:
        all_epics = self.epics.load()
        if epic_id not in all_epics:
            raise ClaimError(f"{epic_id} tapılmadı")
        if all_epics[epic_id]["owner"] != self.user:
            raise ClaimError(f"{epic_id} səndə deyil (işləyən: @{all_epics[epic_id]['owner'] or 'heç kim'})")
        if all_epics[epic_id]["status"] == "done":
            raise ClaimError(f"{epic_id} bitib — buraxmağa ehtiyac yoxdur")
        self.epics._set_front(epic_id, owner="", status="todo")
        self.epics.regenerate_board()
        self.log(f"{epic_id} buraxıldı")

    def task(self, key: str, status: str) -> None:
        if status not in STATUSES:
            raise ClaimError(f"status yalnız {', '.join(STATUSES)} ola bilər")
        all_epics = self.epics.load()
        epic_id = key.split("-")[0]
        epic = all_epics.get(epic_id)
        task = next((t for t in (epic or {}).get("tasks", []) if t["key"] == key), None)
        if task is None:
            raise ClaimError(f"{key} tapılmadı")
        if epic["owner"] != self.user and "parallel-ok" not in task["extra_labels"]:
            owner = f"@{epic['owner']}" if epic["owner"] else "heç kimə"
            raise ClaimError(f"{epic_id} {owner} məxsusdur — əvvəlcə `scripts/epic.py claim {epic_id}`")
        self.epics._set_task(key, status)
        live = [t["status"] if t["key"] != key else status for t in epic["tasks"]]
        if all(s in ("done", "dropped") for s in live) and epic["status"] != "done":
            self.epics._set_front(epic_id, status="done")
            self.log(f"{epic_id}-in bütün task-ları bitdi → epic done (tag-ı `epic-progress` workflow-u qoyur)")
        self.epics.regenerate_board()
        self.log(f"{key}: {task['status']} → {status}")


def current_user(explicit: str | None) -> str:
    if explicit:
        return explicit
    if os.environ.get("QONAQET_DEV"):
        return os.environ["QONAQET_DEV"]
    git = subprocess.run(["git", "config", "github.user"], capture_output=True, text=True)
    if git.returncode == 0 and git.stdout.strip():
        return git.stdout.strip()
    gh = subprocess.run(["gh", "api", "user", "--jq", ".login"], capture_output=True, text=True)
    if gh.returncode == 0 and gh.stdout.strip():
        return gh.stdout.strip()
    raise SystemExit("GitHub login-in müəyyən olunmadı — `--user <login>`, QONAQET_DEV və ya `git config github.user` təyin et")


def main(argv: list[str] | None = None, epics: Epics | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["status", "next", "claim", "release", "task"])
    parser.add_argument("target", nargs="?", help="E05 (claim/release) və ya E05-T03 (task)")
    parser.add_argument("status", nargs="?", help="task üçün: todo | in-progress | done | dropped")
    parser.add_argument("--user")
    args = parser.parse_args(argv)
    if args.command in ("claim", "release") and not (args.target and re.fullmatch(r"E\d{2}", args.target)):
        parser.error("epic açarı lazımdır, məs. E05")
    if args.command == "task" and not (args.target and re.fullmatch(r"E\d{2}-T\d{2}", args.target) and args.status):
        parser.error("istifadə: task E05-T03 in-progress")
    epics = epics or Epics()
    needs_user = args.command in ("claim", "release", "task")
    claims = Claims(epics, current_user(args.user) if needs_user else (args.user or ""))
    try:
        if args.command == "status":
            print(claims.status())
        elif args.command == "next":
            epic = claims.next()
            print(f"{epic['id']} — {epic['title']}" if epic else "götürülə bilən epic yoxdur")
        elif args.command == "claim":
            claims.claim(args.target)
        elif args.command == "release":
            claims.release(args.target)
        else:
            claims.task(args.target, args.status)
    except ClaimError as error:
        print(f"XƏTA: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
