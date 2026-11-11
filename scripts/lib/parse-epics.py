#!/usr/bin/env python3
"""docs/epics/E*.md fayllarını JSON-a çevirir.

Yalnız standart kitabxana işlədilir (CLAUDE.md → Dependency policy).
İstifadə:  python3 scripts/lib/parse-epics.py [docs/epics]
Çıxış:     stdout-a JSON massiv
"""
import json
import pathlib
import re
import sys

AREAS = {
    "backend", "integration", "mobile", "web-landing",
    "web-business", "web-admin", "infra", "design",
}
PRIORITIES = {"p0", "p1", "p2"}
SIZES = {"XS", "S", "M", "L"}
KNOWN_LABELS = {"parallel-ok", "blocked-ui-guidelines"}

TASK_RE = re.compile(r"^###\s+(?P<key>E\d{2}-T\d{2})\s+·\s+(?P<title>.+?)\s*$")
META_RE = re.compile(r"^-\s+(?P<key>area|priority|size|labels|done):\s*(?P<val>.+?)\s*(?:#.*)?$")


def fail(msg):
    print(f"parse-epics: {msg}", file=sys.stderr)
    sys.exit(1)


def split_front_matter(text, path):
    if not text.startswith("---\n"):
        fail(f"{path}: front matter tapılmadı")
    end = text.find("\n---\n", 3)
    if end == -1:
        fail(f"{path}: front matter bağlanmayıb")
    raw = text[4:end]
    rest = text[end + 5:]
    meta = {}
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        if ":" not in line:
            fail(f"{path}: front matter sətri səhvdir: {line!r}")
        k, v = line.split(":", 1)
        meta[k.strip()] = v.strip().strip('"')
    return meta, rest


def section(body, name):
    """## <name> başlığından növbəti ## başlığına qədər olan mətn."""
    pat = re.compile(rf"^##\s+{re.escape(name)}\s*$", re.M)
    m = pat.search(body)
    if not m:
        return ""
    start = m.end()
    nxt = re.compile(r"^##\s+", re.M).search(body, start)
    return body[start:nxt.start() if nxt else len(body)].strip()


def parse_tasks(body, epic_id, path):
    tasks_block = section(body, "Tasks")
    if not tasks_block:
        fail(f"{path}: '## Tasks' bölməsi yoxdur")
    chunks = re.split(r"^(?=###\s+)", tasks_block, flags=re.M)
    tasks = []
    for chunk in chunks:
        chunk = chunk.strip()
        if not chunk.startswith("###"):
            continue
        lines = chunk.splitlines()
        m = TASK_RE.match(lines[0])
        if not m:
            fail(f"{path}: task başlığı formatı səhvdir: {lines[0]!r}\n"
                 f"  gözlənilir: '### {epic_id}-TNN · Ad'")
        key, title = m.group("key"), m.group("title")
        if not key.startswith(epic_id + "-"):
            fail(f"{path}: {key} epic {epic_id} ilə uyğun gəlmir")

        meta, rest = {}, []
        in_meta = True
        for line in lines[1:]:
            mm = META_RE.match(line) if in_meta else None
            if mm:
                meta[mm.group("key")] = mm.group("val").strip()
                continue
            if in_meta and line.strip() == "":
                if not rest:
                    continue
            if line.strip() and not line.startswith("- "):
                in_meta = False
            rest.append(line)

        area = meta.get("area", "")
        priority = meta.get("priority", "")
        size = meta.get("size", "")
        if area not in AREAS:
            fail(f"{path}: {key} — naməlum area {area!r}")
        if priority not in PRIORITIES:
            fail(f"{path}: {key} — naməlum priority {priority!r}")
        if size not in SIZES:
            fail(f"{path}: {key} — naməlum size {size!r}")

        extra = [s.strip() for s in meta.get("labels", "").split(",") if s.strip()]
        for lbl in extra:
            if lbl not in KNOWN_LABELS:
                fail(f"{path}: {key} — naməlum label {lbl!r} "
                     f"(tanınanlar: {', '.join(sorted(KNOWN_LABELS))})")

        tasks.append({
            "key": key,
            "title": title,
            "area": area,
            "priority": priority,
            "size": size,
            "extra_labels": extra,
            "done": meta.get("done", "").lower() == "true",
            "body": "\n".join(rest).strip(),
        })
    if not tasks:
        fail(f"{path}: heç bir task tapılmadı")
    return tasks


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "docs/epics")
    out = []
    seen_keys = set()
    for path in sorted(root.glob("E*.md")):
        text = path.read_text(encoding="utf-8")
        meta, body = split_front_matter(text, path)
        epic_id = meta.get("id", "")
        if epic_id != path.stem:
            fail(f"{path}: front matter id ({epic_id!r}) fayl adı ilə uyğun gəlmir")
        if meta.get("area") not in AREAS:
            fail(f"{path}: naməlum area {meta.get('area')!r}")
        if meta.get("priority") not in PRIORITIES:
            fail(f"{path}: naməlum priority {meta.get('priority')!r}")
        tasks = parse_tasks(body, epic_id, path)
        for t in tasks:
            if t["key"] in seen_keys:
                fail(f"{path}: təkrarlanan task açarı {t['key']}")
            seen_keys.add(t["key"])
        out.append({
            "id": epic_id,
            "title": meta["title"],
            "area": meta["area"],
            "priority": meta["priority"],
            "file": str(path),
            "goal": section(body, "Goal"),
            "scope": section(body, "Scope"),
            "out_of_scope": section(body, "Out of scope"),
            "acceptance": section(body, "Acceptance criteria"),
            "tasks": tasks,
        })
    if not out:
        fail(f"{root}: epic faylı tapılmadı")
    json.dump(out, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
