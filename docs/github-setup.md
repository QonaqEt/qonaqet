# GitHub quraşdırması

`CLAUDE.md` → "GitHub automation" bölməsinin icra təlimatı. Bir dəfə qurulur, sonra `repo-settings`
workflow-u parametrləri saxlayır.

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

Repo parametrlərini və ruleset-i yalnız admin dəyişə bilər; `GITHUB_TOKEN` kifayət deyil. Ona görə
`repo-settings.yml` admin istifadəçinin token-ini işlədir (adı tarixi səbəbdən `PROJECT_TOKEN`-dir).

1. https://github.com/settings/tokens → **Generate new token (classic)** — scope: **`repo`**, müddət 90 gün
2. **Settings → Secrets and variables → Actions → New repository secret** — ad `PROJECT_TOKEN`

> ⚠️ Repo secret-ini həmin repodakı **hər** workflow oxuya bilər. `.github/**` `CODEOWNERS` ilə qorunur.

## 3. İş izləmə

GitHub Project (kanban) və issue-lar planlaşdırma üçün **işlədilmir** (ADR 0004). Epic və task-lar
`docs/epics/*.md`-dədir, ümumi görünüş `docs/BOARD.md`-dir. Köhnə issue-lar və "QONAQET" board-u
tarixçə kimi qalır. İş prosesi: `CLAUDE.md` → "How Claude Code works".

## 4. Label-lar

`area:*` label-larını `actions/labeler` PR-lara qoyur (`.github/labeler.yml`); Dependabot da onları
işlədir. Label-lar repo-da artıq mövcuddur.

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

və ya **Actions → repo-settings → Run workflow → `check | apply`** (`PROJECT_TOKEN` ilə işləyir).

Nə qurulur:

- **Ruleset `main`**: PR məcburidir, 1 approval + code owner rəyi, açıq rəy thread-ləri bağlanmalıdır,
  linear history, yalnız squash, signed commits, force push və budağın silinməsi qadağandır,
  bypass edən yoxdur
- **Merge**: yalnız squash; head budaqları avtomatik silinir; auto-merge açıqdır
- **Təhlükəsizlik**: secret scanning + push protection, Dependabot alerts, private vulnerability reporting
- **Actions**: action-ların tam commit SHA ilə pinlənməsi tələbi

### Plan

Repo **public**-dir — ruleset, CODEOWNERS məcburiyyəti, auto-merge, secret scanning, CodeQL və
dependency review pulsuz mövcuddur. Repo yenidən private edilsə, bunlar Free planda itir (ruleset və
CODEOWNERS üçün ən azı Team, secret scanning/CodeQL üçün əlavə olaraq Code Security lazımdır);
`--check` bunları "plan icazə vermir" kimi göstərəcək.

### Tələb olunan check-lər

`required-checks.txt`-ə check yalnız onun workflow-u `main`-də olandan sonra əlavə olunur: heç vaxt
status verməyən tələb olunan check bütün PR-ları bloklayır. Ona görə CI workflow-ları path filtrini
trigger səviyyəsində (`on.pull_request.paths`) yox, **job səviyyəsində** saxlayır — buraxılan job
"skipped" kimi uğurlu sayılır.

### Rəy qaydası (iki developer)

Ruleset-də bypass yoxdur: hər PR-ı **digər** developer təsdiqləyir (GitHub müəllifin öz PR-ını
təsdiqləməsinə icazə vermir). `.github/**`, `scripts/**`, `docs/dependencies/**`, `docs/adr/**` və
`CLAUDE.md` dəyişikliklərində təsdiqləyən `@QonaqEt/owners` komandasının üzvü olmalıdır — ona görə hər
iki developer komandada olmalıdır. Claude Code PR-ı yalnız required check-lər yaşıl və PR təsdiqlənmiş
olanda birləşdirir.

## 6. CODEOWNERS

`.github/CODEOWNERS` `@QonaqEt/owners` komandasına istinad edir.

> ⚠️ Komanda mövcud deyilsə **CODEOWNERS səssizcə işləmir** — heç bir xəbərdarlıq verilmir.
> Ya Org → Teams → `owners` komandasını yarat (repo-ya write icazəsi ilə),
> ya da fayldakı sahibi öz GitHub istifadəçi adınla əvəz et.

## 7. Növbəti addım

Board hazır olandan sonra iş `CLAUDE.md` → "How Claude Code works" qaydası ilə gedir:
epic-bə-epic, `E00`-dan başlayaraq; hər task üçün branch, hər PR bir task bağlayır.
