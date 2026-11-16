# Epics

Hər fayl bir epic-dir və **işin yeganə izləmə yeridir** (ADR 0004). Ümumi görünüş `docs/BOARD.md`-dir —
`scripts/board.py` onu bu fayllardan yaradır.

- epic-in vəziyyəti front matter-dədir: `owner` (GitHub login, boş = sərbəst) və `status`
- hər task-ın vəziyyəti `- status:` sətridir
- dəyişmək üçün: `scripts/epic.py claim|release|task`, sonra `scripts/board.py`

Fayllar **maşınla parse olunur**, ona görə format dəqiq saxlanmalıdır.

## Format

```markdown
---
id: E00
title: Epic-in adı
area: infra
priority: p0
depends: E01, E02            # bu epic-dən əvvəl done olmalı olan epic-lər (boş ola bilər)
owner: zahid2000             # işləyən developer (boş = sərbəst)
status: in-progress          # todo | in-progress | done
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
- status: todo               # todo | in-progress | done | dropped

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
