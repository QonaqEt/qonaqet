# GitHub quraşdırması

`CLAUDE.md` → "GitHub automation" bölməsinin icra təlimatı. Bir dəfə qurulur, sonra `bootstrap`
workflow-u qalanını edir.

## 1. Actions

**Settings → Actions → General**

- Actions permissions: *Allow all actions and reusable workflows*
- ☑ **Require actions to be pinned to a full-length commit SHA** — `CLAUDE.md`-dəki pin qaydasını
  platformanın özünə tətbiq etdirir
- Workflow permissions: **default (read-only)**. Hər workflow lazım olan icazəni öz içində açıq yazır:
  ```yaml
  permissions:
    contents: read
    issues: write
  ```
- ☐ Allow GitHub Actions to create and approve pull requests — lazım deyil

## 2. PROJECT_TOKEN

Projects (v2) yalnız GraphQL ilə idarə olunur və `GITHUB_TOKEN` ona yaza bilmir. Ona görə ayrıca token lazımdır.

1. https://github.com/settings/tokens → **Generate new token (classic)**
   - Scope-lar: **`repo`**, **`read:org`**, **`project`**
   - Expiration: 90 gün
2. **Settings → Secrets and variables → Actions → New repository secret**
   - Ad: `PROJECT_TOKEN`

> ⚠️ Repo secret-ini həmin repodakı **hər** workflow oxuya bilər. Ona görə `.github/**` üzərində
> `CODEOWNERS` qoruması olmadan bu token-i əlavə etmə. Uzunmüddətli daha təmiz variant: org-da
> ayrıca GitHub App (`organization_projects: write`) + `actions/create-github-app-token`.

## 3. Bootstrap

**Actions → bootstrap → Run workflow**

Nə edir:

- 17 label yaradır (`epic`, `task`, `bug`, `area:*`, `p0`–`p2`, `parallel-ok`, `blocked-ui-guidelines`, `dependency-approved`)
- `docs/epics/E00.md … E20.md` fayllarından **21 epic** issue-su yaradır
- hər epic-in task-larını **sub-issue** kimi yaradır və bağlayır (**165 task**)
- `done: true` işarəli task-ları yaradıb dərhal bağlayır
- "QONAQET" Project (v2) board-unu yaradır: `Backlog → Ready → In Progress → In Review → Done`
  və `Epic`, `Area`, `Priority`, `Size` sahələri
- bütün issue-ları board-a `Backlog` statusu ilə əlavə edir

İdempotentdir — təkrar işə salmaq dublikat yaratmır. `dry_run` ilə əvvəlcədən yoxlaya bilərsən.

Lokal olaraq da işləyir:

```bash
DRY_RUN=1 REPO=QonaqE/qonaqet ./scripts/bootstrap-github.sh
```

## 4. Project built-in workflow-ları

Board → **⋯ → Workflows**, bunları aktiv et:

| Workflow | Nəticə |
|---|---|
| Item added to project | Status → `Backlog` |
| Item closed | Status → `Done` |
| Pull request merged | Status → `Done` |
| Item reopened | Status → `In Progress` |
| Auto-add to project | `QonaqE/qonaqet` repo-sundakı yeni issue-lar avtomatik düşür |

"Auto-add" aktiv olduqda sonradan yaradılan issue-lar board-a özü düşür — `PROJECT_TOKEN` lazım olmur.

## 5. main ruleset

**Settings → Rules → Rulesets → New branch ruleset**, hədəf `main`:

- ☑ Require a pull request before merging — 1 approval (owner)
- ☑ Require status checks to pass — CI-lar yaşıl olandan sonra əlavə et
- ☑ Require linear history (yalnız squash merge)
- ☑ Require signed commits
- ☑ Block force pushes
- ☐ Restrict deletions — açıq saxla

**Settings → General → Pull Requests**: ☑ Allow squash merging (yalnız), ☑ Automatically delete head
branches, ☑ Allow auto-merge.

## 6. Təhlükəsizlik

**Settings → Code security**

- ☑ Secret scanning + ☑ Push protection
- ☑ Dependabot alerts
- ☑ Private vulnerability reporting

## 7. CODEOWNERS

`.github/CODEOWNERS` `@QonaqE/owners` komandasına istinad edir.

> ⚠️ Komanda mövcud deyilsə **CODEOWNERS səssizcə işləmir** — heç bir xəbərdarlıq verilmir.
> Ya Org → Teams → `owners` komandasını yarat (repo-ya write icazəsi ilə),
> ya da fayldakı sahibi öz GitHub istifadəçi adınla əvəz et.

## 8. Növbəti addım

Board hazır olandan sonra iş `CLAUDE.md` → "How Claude Code works" qaydası ilə gedir:
epic-bə-epic, `E00`-dan başlayaraq; hər task üçün branch, hər PR bir task bağlayır.
