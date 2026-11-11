#!/usr/bin/env python3
"""Epic və task issue-ları üçün GitHub REST payload-larını hazırlayır.

İstifadə: python3 scripts/lib/build-payloads.py <epics-dir> <out-dir> <repo-url>
Çıxış:    <out-dir>/payload/<KEY>.json  və  <out-dir>/index.tsv
          index.tsv sütunları: KIND  KEY  EPIC_ID  TITLE  DONE
"""
import json
import pathlib
import subprocess
import sys

here = pathlib.Path(__file__).parent


def main():
    epics_dir, out_dir, repo_url = sys.argv[1], pathlib.Path(sys.argv[2]), sys.argv[3]
    epics = json.loads(subprocess.run(
        [sys.executable, str(here / "parse-epics.py"), epics_dir],
        check=True, capture_output=True, text=True).stdout)

    payload_dir = out_dir / "payload"
    payload_dir.mkdir(parents=True, exist_ok=True)
    index = []

    for e in epics:
        src = f"Mənbə: [`{e['file']}`]({repo_url}/blob/main/{e['file']}) · " \
              f"Qaydalar: [`CLAUDE.md`]({repo_url}/blob/main/CLAUDE.md)"
        body = (f"## Goal\n\n{e['goal']}\n\n"
                f"## Scope\n\n{e['scope']}\n\n"
                f"## Out of scope\n\n{e['out_of_scope']}\n\n"
                f"## Acceptance criteria\n\n{e['acceptance']}\n\n"
                f"---\n\nTask-lar bu epic-in **sub-issue**-larıdır.\n{src}\n")
        title = f"[{e['id']}] {e['title']}"
        (payload_dir / f"{e['id']}.json").write_text(json.dumps({
            "title": title,
            "body": body,
            "labels": ["epic", f"area:{e['area']}", e["priority"]],
        }, ensure_ascii=False), encoding="utf-8")
        index.append(("epic", e["id"], e["id"], title, "0"))

        for t in e["tasks"]:
            tbody = (f"{t['body']}\n\n---\n\n"
                     f"**Valideyn epic:** {e['id']} — {e['title']}\n"
                     f"**Size:** {t['size']}\n\n{src}\n")
            ttitle = f"[{t['key']}] {t['title']}"
            (payload_dir / f"{t['key']}.json").write_text(json.dumps({
                "title": ttitle,
                "body": tbody,
                "labels": ["task", f"area:{t['area']}", t["priority"]] + t["extra_labels"],
            }, ensure_ascii=False), encoding="utf-8")
            index.append(("task", t["key"], e["id"], ttitle, "1" if t["done"] else "0"))

    (out_dir / "index.tsv").write_text(
        "".join("\t".join(r) + "\n" for r in index), encoding="utf-8")
    print(f"{sum(1 for r in index if r[0]=='epic')} epic, "
          f"{sum(1 for r in index if r[0]=='task')} task payload hazırdır", file=sys.stderr)


if __name__ == "__main__":
    main()
