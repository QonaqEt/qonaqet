# Epics

Hər fayl bir epic-dir. `scripts/bootstrap-github.sh` bu faylları oxuyub GitHub-da yaradır:

- epic → `epic` label-li issue
- hər task → həmin epic-in **sub-issue**-su (`task` + `area:*` + `p0|p1|p2` label-ları ilə)

Fayllar **maşınla parse olunur**, ona görə format dəqiq saxlanmalıdır.

## Format

```markdown
---
id: E00
title: Epic-in adı
area: infra
priority: p0
depends: E01, E02            # bu epic-dən əvvəl bağlanmalı olan epic-lər (boş ola bilər)
---

## Goal
Bir-iki cümlə.

## Scope
- ...

## Out of scope
- ...

## Acceptance criteria
- [ ] ...

## Tasks

### E00-T01 · Task-ın adı
- area: infra
- priority: p0
- size: S
- labels: parallel-ok        # ixtiyari, vergüllə ayrılır
- done: true                 # ixtiyari — artıq yerinə yetirilibsə, bootstrap issue-nu yaradıb dərhal bağlayır

Context: bir abzas — nə və niyə.

Acceptance:
- [ ] yoxlana bilən nəticə
- [ ] ...
```

## Qaydalar

- `id` fayl adı ilə üst-üstə düşməlidir (`E07.md` → `id: E07`).
- `depends` yalnız mövcud epic-lərə istinad edir, dövr ola bilməz (parser yoxlayır). `scripts/epic.py` epic-in götürülə bilib-bilmədiyini buna görə müəyyən edir.
- Task başlığı həmişə `### <EPIC>-T<NN> · <ad>` şəklindədir.
- `area` yalnız bunlardan biri: `backend`, `integration`, `mobile`, `web-landing`, `web-business`, `web-admin`, `infra`, `design`.
- `size`: `XS` | `S` | `M` | `L`.
- Tanınan əlavə `labels`:
  - `parallel-ok` — epic sırasını gözləmədən paralel götürülə bilər
  - `blocked-ui-guidelines` — `CLAUDE.md` → "Angular UI guidelines" bölməsi doldurulana qədər **başlanmır**
- Yeni iş çıxırsa — düzgün epic-ə yeni task əlavə et, scope-u səssizcə genişləndirmə (`CLAUDE.md` → "How Claude Code works", 6).
- `CLAUDE.md`-dən kənara çıxan arxitektura qərarı `docs/adr/` tələb edir.
