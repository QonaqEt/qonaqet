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
DRY_RUN=1 REPO=QonaqEt/qonaqet ./scripts/bootstrap-github.sh
```

## 4. Project built-in workflow-ları

Board → **⋯ → Workflows**, bunları aktiv et:

| Workflow | Nəticə |
|---|---|
| Item added to project | Status → `Backlog` |
| Item closed | Status → `Done` |
| Pull request merged | Status → `Done` |
| Item reopened | Status → `In Progress` |
| Auto-add to project | `QonaqEt/qonaqet` repo-sundakı yeni issue-lar avtomatik düşür |

"Auto-add" aktiv olduqda sonradan yaradılan issue-lar board-a özü düşür — `PROJECT_TOKEN` lazım olmur.

Built-in workflow-ların etmədiyini `project-sync.yml` edir (`PROJECT_TOKEN` ilə):

| Hadisə | Nəticə |
|---|---|
| `feat\|fix\|chore/<issue#>-<slug>` branch-ı yaradılır | task → `In Progress` (yalnız `Backlog`/`Ready`-dən; geri çəkilmir) |
| issue açılır, label/body dəyişir | `Epic` (valideyn epic-dən), `Area`/`Priority` (label-lardan), `Size` (body-dən) |
| **Actions → project-sync → Run workflow** | bir issue və ya (boş saxlasan) bütün issue-lar üçün backfill |

## 5. Repo parametrləri və main ruleset

Bütün parametrlər kodda saxlanılır və bir skriptlə tətbiq olunur (yalnız REST, idempotent):

| Fayl | Nədir |
|---|---|
| `.github/rulesets/main.json` | `main` ruleset-inin tərifi |
| `.github/rulesets/required-checks.txt` | tələb olunan status check-lər (hər sətirdə bir job adı) |
| `scripts/repo-settings.py` | `--check` (yalnız oxuyur, fərq varsa 1 ilə çıxır) və `--apply` |

```bash
scripts/repo-settings.py --check     # cari vəziyyət cədvəli
scripts/repo-settings.py --apply     # repo admin token-i tələb edir
```

və ya **Actions → bootstrap → Run workflow → `repo_settings: check | apply`** (`PROJECT_TOKEN` ilə işləyir).

Nə qurulur:

- **Ruleset `main`**: PR məcburidir, 1 approval + code owner rəyi, açıq rəy thread-ləri bağlanmalıdır,
  linear history, yalnız squash, signed commits, force push və budağın silinməsi qadağandır,
  bypass edən yoxdur
- **Merge**: yalnız squash; head budaqları avtomatik silinir; auto-merge açıqdır
- **Təhlükəsizlik**: secret scanning + push protection, Dependabot alerts, private vulnerability reporting
- **Actions**: action-ların tam commit SHA ilə pinlənməsi tələbi

### ⚠️ Plan tələbi

Repo **private**-dir və təşkilat **Free** plandadır. Bu kombinasiyada GitHub aşağıdakıları **vermir**
(API `403 Upgrade to GitHub Pro or make this repository public` qaytarır):

| Xüsusiyyət | Free + private | Team + private | Public |
|---|---|---|---|
| Ruleset / branch protection | ✗ | ✓ | ✓ |
| CODEOWNERS-in məcburi rəyi | ✗ | ✓ | ✓ |
| Auto-merge | ✗ | ✓ | ✓ |
| Secret scanning + push protection | ✗ | ödənişli əlavə (Secret Protection) | ✓ |
| CodeQL (code scanning), dependency review | ✗ | ödənişli əlavə (Code Security) | ✓ |

Yəni hazırda `main`-ə birbaşa push mümkündür və `CODEOWNERS` yalnız rəyçi təklif edir, heç nəyi
bloklamır — `PROJECT_TOKEN`-i qoruyan şərt (bax bölmə 2) faktiki işləmir. Skript bu parametrləri
"plan icazə vermir" kimi göstərir və 1 ilə çıxır; plan dəyişəndən sonra `--apply` qalanını qurur.

### Tələb olunan check-lər

`required-checks.txt`-ə check yalnız onun workflow-u `main`-də olandan sonra əlavə olunur: heç vaxt
status verməyən tələb olunan check bütün PR-ları bloklayır. Ona görə CI workflow-ları path filtrini
trigger səviyyəsində (`on.pull_request.paths`) yox, **job səviyyəsində** saxlayır — buraxılan job
"skipped" kimi uğurlu sayılır.

### Tək sahibli təşkilat üçün qeyd

Ruleset 1 approval tələb edir və bypass yoxdur. GitHub müəllifin öz PR-ını təsdiqləməsinə icazə
vermir, ona görə sahibin **öz hesabından açılan** PR-ları ikinci rəyçi olmadan birləşdirmək olmaz.
İkinci rəyçi yoxdursa, `main.json`-dakı `bypass_actors`-a
`{"actor_id": 1, "actor_type": "OrganizationAdmin", "bypass_mode": "pull_request"}` əlavə et
(PR yenə məcburidir, yalnız approval tələbi sahib üçün götürülür) — bu, `CLAUDE.md`-dən kənarlaşmadır
və ADR tələb edir.

## 6. CODEOWNERS

`.github/CODEOWNERS` `@QonaqEt/owners` komandasına istinad edir.

> ⚠️ Komanda mövcud deyilsə **CODEOWNERS səssizcə işləmir** — heç bir xəbərdarlıq verilmir.
> Ya Org → Teams → `owners` komandasını yarat (repo-ya write icazəsi ilə),
> ya da fayldakı sahibi öz GitHub istifadəçi adınla əvəz et.

## 7. Növbəti addım

Board hazır olandan sonra iş `CLAUDE.md` → "How Claude Code works" qaydası ilə gedir:
epic-bə-epic, `E00`-dan başlayaraq; hər task üçün branch, hər PR bir task bağlayır.
