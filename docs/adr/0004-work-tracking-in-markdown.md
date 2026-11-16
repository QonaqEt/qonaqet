# 0004 — İş izləmə repo-dakı markdown-dadır (GitHub Project və issue-lar yox)

- **Status:** Accepted
- **Tarix:** 2026-11-16
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E00-T18, ADR 0001

## Kontekst

`CLAUDE.md` əvvəlcə işi GitHub-da idarə edirdi: hər epic və task issue idi (sub-issue-lar), status
"QONAQET" Project (v2) board-unda saxlanılırdı, `project-sync`/`epic-progress` workflow-ları onları
sinxronlaşdırırdı. Təcrübədə:

- Projects v2 yalnız GraphQL ilə idarə olunur və ayrıca admin token (`PROJECT_TOKEN`) tələb edir;
  Claude Code sessiyalarından GraphQL bloklanıb — board-u yalnız Actions runner-ində dəyişmək olurdu.
- Həqiqət iki yerdə idi: `docs/epics/*.md` (task mətni) və GitHub (status), bir-birindən ayrılırdı.
- İki developer (hər biri öz Claude sessiyası ilə) üçün "kim hansı epic-dədir" sualının cavabı repo-da yox idi.

Repo sahibi qərar verdi: kanban artıq istifadə olunmur, iş lokal markdown-da izlənir.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. GitHub Project + issue-lar (əvvəlki) | Vizual board, bildirişlər | İki həqiqət mənbəyi, GraphQL/token asılılığı, Claude-dan əlçatmaz |
| B. Hər şey `docs/epics/*.md`-də; board generasiya olunan `docs/BOARD.md` | Tək mənbə, PR ilə versiyalanır, offline, hər alət oxuya bilər | Status dəyişikliyi commit tələb edir; bildiriş yoxdur |
| C. B + issue-lar yalnız bildiriş üçün | Bildiriş qalır | Yenə iki mənbə |

## Qərar

**B.**

- Epic = `docs/epics/Exx.md`; front matter-də `owner` (GitHub login) və `status` (`todo|in-progress|done`).
- Task = epic faylındakı `### Exx-Tnn` bölməsi, `- status: todo|in-progress|done|dropped`.
- `docs/BOARD.md` `scripts/board.py` ilə generasiya olunur; CI (`ci-infra / scripts`) aktual olmasını yoxlayır.
- `scripts/epic.py claim|release|task` statusları dəyişir; dəyişiklik aid olduğu PR-da commit olunur.
- `pr-checks`: PR body-də `Task: Exx-Tnn`, task mövcuddur, epic PR müəllifinindir və `main`-də başqasına
  məxsus deyil. Eyni epic-i iki developer eyni anda götürsə, ikinci PR birinci birləşəndən sonra qırılır.
- `epic-progress`: `status: done` olan epic-ə `epic-Exx-done` tag-ı.
- Çıxarılır: `project-sync.yml`, issue/board bootstrap-ı (`bootstrap-github.sh`, `bootstrap-project.sh`),
  epic/task issue şablonları. `bootstrap.yml` → `repo-settings.yml` (yalnız repo parametrləri).
- GitHub issue-ları yalnız kənardan gələn bug-lar üçündür. Köhnə issue-lar və board tarixçə kimi qalır
  (silinmir, bağlanmır).

## Nəticələr

- Müsbət: tək həqiqət mənbəyi; Claude sessiyası statusu birbaşa oxuyur və dəyişir; token tələb olunmur.
- Mənfi / qəbul edilən risk: status yalnız PR birləşəndə `main`-də görünür; iki developer eyni epic-i
  eyni anda götürsə, toqquşma PR səviyyəsində (ikincinin pr-checks-i) aşkarlanır, claim anında yox.
  Yumşaltma: claim-i ayrıca kiçik PR kimi tez birləşdirmək mümkündür.
- `CLAUDE.md`-də dəyişən bölmələr: "GitHub automation", "Work management", "How Claude Code works", "Coding rules".

## Yenidən baxış

Komanda ikidən çox developerə çatanda və ya claim toqquşmaları təkrarlananda.
