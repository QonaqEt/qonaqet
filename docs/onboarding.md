# Komandaya qoşulma

QONAQET-ə xoş gəldin. Bu sənəd ilk günün yol xəritəsidir: nəyə giriş lazımdır, nə oxumalısan,
iş necə gedir və hansı qaydalar danışılmazdır.

> **Vəziyyət (2026-11-12):** repo **skeleton** mərhələsindədir. Qovluq quruluşu, sənədlər və
> GitHub avtomatlaşdırması hazırdır; **məhsul kodu hələ yazılmayıb**. `E00` epic-i işləyir.
> Aşağıda "hələ yoxdur" yazılan şeylər `E00` tamamlananda görünəcək.

---

## 1. Giriş icazələri

- [ ] **GitHub org dəvəti** — `QonaqEt` təşkilatı
- [ ] **Repo girişi** — [`QonaqEt/qonaqet`](https://github.com/QonaqEt/qonaqet) (public)
- [ ] **2FA aktivdir** (org tələbidir)
- [ ] `@QonaqEt/owners` komandasına əlavə olunmaq — digər developerin PR-larını (o cümlədən `.github/**`, `scripts/**`, `CLAUDE.md`) təsdiqləmək üçün lazımdır

İcazə verən: repo sahibi.

## 2. Oxumalı olduqların

Sıra ilə, kod yazmazdan **əvvəl**:

| # | Sənəd | Nə üçün |
|---|---|---|
| 1 | [`CLAUDE.md`](../CLAUDE.md) | **Yeganə həqiqət mənbəyi.** Arxitektura, stack, domen modeli, biznes qaydaları, asılılıq siyasəti, iş prosesi |
| 2 | [`docs/epics/README.md`](./epics/README.md) | Epic/task formatı |
| 3 | Öz sahənin epic faylı | `docs/epics/E__.md` — məqsəd, scope, acceptance criteria |
| 4 | [`docs/adr/`](./adr/) | Qəbul edilmiş kənaraçıxmalar (0001 bazis, 0002 RustFS) |
| 5 | [`docs/github-setup.md`](./github-setup.md) | Repo parametrləri, workflow-lar |

`CLAUDE.md` böyükdür. Rolun üzrə mütləq oxu:

- **Backend** → Architecture overview · Domain model · Core business rules · Backend structure · Coding rules
- **Mobil** → Tech stack (Mobile) · Mobile app · Core business rules (A, B) · Coding rules
- **Veb** → Tech stack (Web) · Web apps · Brand, colors and the landing page · **Angular UI guidelines**
- **İnfra** → Dependency policy · GitHub automation · Architecture overview

Hamı üçün məcburi: **Coding rules** və **Soft delete**.

## 3. Lokal mühit

```bash
git clone https://github.com/QonaqEt/qonaqet
cd qonaqet
```

**Hazırda qurulacaq bir şey yoxdur** — solution, Angular workspace, Flutter layihəsi və
`docker-compose` hələ mövcud deyil. Onlar `E00-T02` və `E01-T01` ilə gəlir.

Gələndə lazım olacaq (indidən quraşdıra bilərsən):

| Alət | Versiya |
|---|---|
| .NET SDK | 10 (LTS) |
| Node.js | aktiv LTS + Angular CLI |
| Flutter | stable, Dart 3 |
| Docker + Compose | cari |
| `gh` CLI | cari |

`E00-T02` bitəndən sonra bütün dev stack bir əmrlə qalxacaq:

```bash
docker compose -f deploy/docker-compose.yml up
```

## 4. İş necə gedir

**Epic → task → PR.** Epic-lər və task-lar repo-dadır: `docs/epics/Exx.md`, ümumi görünüş
[`docs/BOARD.md`](BOARD.md) (ADR 0004). GitHub Project/issue-lar planlaşdırma üçün işlədilmir.

İki developer eyni epic-də işləmir: epic-in sahibi front matter-dəki `owner`-dir.

1. **Epic götür** — `scripts/epic.py status`, sonra `scripts/epic.py next` və `scripts/epic.py claim Exx`.
   Səndə açıq epic varsa, onu davam etdir. Başqasının epic-inə toxunma
   (istisna: `parallel-ok` task-lar).
2. **Task götür** — epic-in ən yuxarıdakı `todo` task-ı → `scripts/epic.py task Exx-Tnn in-progress`.
3. **Branch:** `feat/Exx-Tnn-<slug>` · `fix/...` · `chore/...`
4. **Testlərlə yaz.** Test olmadan PR açma.
5. **Commit:** Conventional Commits, task açarı ilə — `feat(gifts): partial voucher redemption (E09-T07)`
6. **Bitəndə:** `scripts/epic.py task Exx-Tnn done` — `docs/BOARD.md` özü yenilənir; ikisini də commit et.
7. **PR:** şablonu doldur, body-də `Task: Exx-Tnn`. UI dəyişikliyi varsa skrinşot məcburidir.
8. **CI yaşıl olmalıdır:** build, testlər, lint, arxitektura testləri, `pr-checks`.
9. **Digər developer təsdiqləyir**, sonra squash merge.

Epic-in bütün task-ları bitəndə: acceptance criteria yoxlanılır, `README.md`/`docs/` yenilənir,
epic `status: done` olur (sonuncu `task ... done` bunu edir), birləşəndən sonra `epic-Exx-done` tag-ı
avtomatik yaradılır.

**Yeni iş çıxırsa** — düzgün epic faylına `### Exx-Tnn · …` bölməsi (`- status: todo`) əlavə et və
`scripts/board.py` işlət. Scope-u səssizcə genişləndirmə.

## 5. Danışılmaz qaydalar

Bunlar PR-da rədd səbəbidir və bir qismini arxitektura testləri build-i qıraraq yoxlayır.

1. **Heç vaxt hard delete yoxdur** — nə Mongo, nə Postgres, nə obyekt saxlama. TTL index də yox.
   Yalnız `SoftDeleteAsync` / `RestoreAsync`.
2. **Pul həmişə `long` qəpikdir** və yalnız serverdə hesablanır. Klientdən gələn məbləğ qəbul edilmir.
3. **Store-lar arası effektlər yalnız outbox event** ilə. Distributed tranzaksiya yoxdur.
   Consumer-lər idempotentdir.
4. **Kateqoriya kodu və ya adı üzrə şərt yazma.** Bütün davranış kateqoriya sənədindən gəlir.
5. **Hər yeni indeks partial-dır** — Mongo `{ isDeleted: false }`, Postgres `WHERE is_deleted = false`.
6. **Allowlist-dən kənar paket yoxdur.** Əlavə etmək ADR + `dependency-approved` label-ı tələb edir.
7. **Rənglər və şriftlər yalnız `/design-tokens`-dan.** Komponentdə hard-code hex yoxdur.
8. **Loglama:** OTP (Development istisna), token, hədiyyə link token-i, FIN, sənəd məzmunu,
   kart məlumatı **heç vaxt** loglanmır.
9. **Tərəflər arası şəxsi məlumat sızmamalıdır** — alıcının məlumatı biznesə/donora/şirkətə,
   göndərənin məlumatı biznesə. Testlər bunu yoxlamalıdır.
10. **Hər pul hərəkəti** idempotentdir, öz outbox event-i ilə eyni tranzaksiyadadır,
    ledger və audit log yazısı yaradır.

C#: nullable enabled, file-scoped namespace, hər yerdə `async` + `CancellationToken`,
controller-də biznes məntiqi yoxdur.

## 6. Yeni asılılıq əlavə etmək

Qısa cavab: **əvvəlcə özümüz yazırıq.** Dispatcher, mapping, validasiya, CSV, şablon mühərriki,
scheduler, rate limiter, QR encoder — hamısı öz kodumuzdur.

Həqiqətən paket lazımdırsa:

1. [`docs/dependencies/allowlist.json`](./dependencies/allowlist.json)-u yoxla — bəlkə artıq var
2. Mənbəsi icazəlidirmi? (platforma vendorları, işlətdiyimiz məhsulun rəsmi SDK-sı,
   pub.dev-də yalnız verified publisher-lər, npm-də sadalanan siyahı)
3. ADR yaz — [`docs/adr/0000-template.md`](./adr/0000-template.md)
4. Allowlist-ə əlavə et
5. PR-a repo sahibindən `dependency-approved` label-ı al

`dependency-guard` CI job-u allowlist-dən kənar paketi tapanda build-i qırır.

## 7. Nə vaxt ADR yazılır

`CLAUDE.md`-dəki qaydadan **kənara çıxanda** və ya ona **istisna** olanda. Adi dizayn seçimi,
adlandırma və refaktorinq üçün yox — onlar PR təsvirində izah olunur.

## 8. Rola görə ilk addım

| Rol | İlk epic | Başlanğıc |
|---|---|---|
| Backend | `E01` Backend foundation | Solution və layerlər; əvvəlcə SharedKernel |
| Mobil | `E14` Mobile foundation | `E02` bitdikdən sonra paralel gedə bilər (`parallel-ok`) |
| Veb | `E17`/`E18`/`E19` | **Yalnız skeleton** — aşağıdakı xəbərdarlığı oxu |
| İnfra | `E00` | docker-compose, ruleset, CI workflow-ları |
| Dizayn | `E00-T15` | `design-tokens/tokens.json` və Style Dictionary pipeline |

## 9. Diqqət

**Angular UI guidelines hələ PENDING-dir.** `CLAUDE.md`-dəki həmin bölmə UI həmkarından gələnə qədər:

- yalnız skeleton, routing, auth, API client, design token-lər və səhifə karkasları qurulur
- **komponent kitabxanası seçilmir və quraşdırılmır**
- portallar üçün son ekranlar uydurulmur
- `blocked-ui-guidelines` etiketli task-lar başlanmır

**Ton.** Hədiyyə axınlarında isti və şən; sosial qonaqlıqda hörmətli. "Kasıb" tipli sözlər
heç vaxt işlənmir. Donor alıcını nə seçir, nə görür. Biznes alıcının kimliyini görmür.
Həssas kateqoriyalar staff-a göstərilmir.

**Dil.** UI Azərbaycancadır (`en`/`ru` üçün struktur hazırdır). Kod identifikatorları ingiliscədir.
Pul qəpikdir, vaxt UTC-də saxlanılır, iş günü **Asia/Baku** üzrədir.

## 10. PROJECT_TOKEN yaratmaq

> **Yalnız admin / owner icazəsi olanlar üçün.** Adi töhfəçiyə lazım deyil.

### Niyə lazımdır

Repo parametrlərini və `main` ruleset-ini yalnız repo admini dəyişə bilər; Actions-ın verdiyi
`GITHUB_TOKEN` kifayət deyil. `repo-settings.yml` workflow-u admin istifadəçinin token-ini işlədir.
Adı tarixi səbəbdən `PROJECT_TOKEN`-dir — GitHub Project board-u artıq işlədilmir (ADR 0004).

### Addımlar

1. https://github.com/settings/tokens → **Generate new token (classic)**
2. **Note:** `QONAQET repo settings` · **Expiration:** 90 gün · **Scope:** `repo`
3. **Generate token** → dəyəri kopyala (bir dəfə göstərilir)
4. Repo → **Settings → Secrets and variables → Actions → New repository secret**
   - Name: `PROJECT_TOKEN`
   - Secret: kopyaladığın dəyər

### Yoxlama

**Actions → repo-settings → Run workflow → `check`.** Cədvəldəki bütün sətirlər ✅ olmalıdır.

### Rotasiya

- **Scope dəyişmək** (məs. `project` əlavə etmək): token-i **Edit** et → scope-u işarələ → Update.
  **Dəyər dəyişmir**, secret-i yenidən əlavə etməyə ehtiyac yoxdur.
- **Regenerate** etsən, dəyər dəyişir — `PROJECT_TOKEN` secret-ini mütləq yenilə.
- Müddəti bitməzdən əvvəl GitHub email göndərir. Bitəndə `repo-settings` workflow-u aydın xəta ilə dayanır.

### Təhlükəsizlik

- **Repo secret-ini o repodakı hər workflow oxuya bilər.** Ona görə `.github/**` üzərində
  `CODEOWNERS` qoruması **məcburidir** — onsuz workflow dəyişən hər kəs token-i sızdıra bilər.
  `@QonaqEt/owners` komandası mövcud olmalıdır, yoxsa `CODEOWNERS` səssizcə işləmir.
- Token-i **heç vaxt** chat-a, issue-ya, PR-a və ya kod bazasına yazma.
  Secret scanning + push protection aktivdir və commit-i bloklayacaq.
- Workflow-da `echo`/`set -x` ilə token-i loglama.
- Workflow permissions repo səviyyəsində **read-only** qalır; hər workflow öz `permissions:`
  blokunu açıq yazır.
- **Uzunmüddətli daha təmiz variant:** org-da ayrıca GitHub App
  (`administration: write`) və `actions/create-github-app-token`.
  Şəxsi token olmur, avtomatik rotasiya olunur, müddəti bitmir.

## 11. Claude Code ilə işləmək

Komandada hər kəs öz Claude Code sessiyası ilə işləyir. `CLAUDE.md` repo kökündə olduğu üçün
**avtomatik yüklənir** — onu hər dəfə yapışdırmağa ehtiyac yoxdur.

### Sessiyanı başlatmaq

- **Bulud:** claude.ai/code → yeni sessiya → mənbə repo kimi `QonaqEt/qonaqet` seç
- **Lokal:** repo kökündə `claude` əmrini işlət

Hər iki halda Claude `CLAUDE.md`-ni, `docs/epics/` və `docs/adr/` qovluqlarını oxuya bilir.

### İlk mesaj

Sessiyanın əvvəlində Claude özü `scripts/epic.py status` işlədir (`CLAUDE.md` → "How Claude Code works").
Sən sadəcə hansı epic-də və ya task-da işlədiyini de. Məsələn:

```
E01-də işləyirik. scripts/epic.py claim E01 et, sonra ən üstdəki todo task-ı götür.
```

və ya konkret task üçün:

```
E01-T04 (öz CQRS dispatcher-imiz) — docs/epics/E01.md-dəki acceptance criteria-ya bax.
Branch aç, testlərlə yaz, body-də "Task: E01-T04" olan PR hazırla.
```

Epic-i kimin işlədiyi `docs/epics/Exx.md`-in `owner` sahəsindədir və `docs/BOARD.md`-də görünür.
İkiniz eyni epic-i götürsəniz, ikinci PR-ın `pr-checks`-i qırılır.

Scope-u açıq saxlama — Claude boş yerə genişləndirməsin deyə task nömrəsini həmişə göstər.

### Bu mühitdə bilinən məhdudiyyətlər

Bulud sessiyalarında vaxt itirməmək üçün:

| Məhdudiyyət | Nə etmək |
|---|---|
| **GitHub GraphQL bloklanıb** | REST işlət. İş izləmə onsuz da lokal markdown-dadır (ADR 0004) |
| **`gh api --paginate` qırılır** | Link başlığı `repositories/{id}` yolunu verir. Açıq `page=N` dövrü ilə səhifələ — nümunə: `scripts/ci/epic-progress.py` tarixçəsi, `scripts/repo-settings.py` |
| **Action-lar SHA ilə pinlənməlidir** | SHA tapmaq: `git ls-remote https://github.com/actions/checkout refs/tags/v5.0.0` |
| **Workflow permissions read-only-dir** | Hər workflow öz `permissions:` blokunu açıq yazır |
| **Org adı dəyişsə, REST qırılır** | Köhnə ad `repositories/{id}`-yə yönləndirilir və bloklanır. Yeni adla yeni sessiya lazımdır |

### Nəyə icazə vermə

- **Allowlist-dən kənar paket quraşdırmaq.** `docs/dependencies/allowlist.json` yoxlanılmalıdır;
  CI onsuz da qıracaq, amma vaxt itirirsən.
- **`CLAUDE.md`-dən kənara çıxmaq** — ADR olmadan. Qaydanı dəyişmək istəyirsə, əvvəlcə ADR.
- **Scope genişləndirmək.** Yeni iş çıxırsa, yeni task açılır.
- **Angular komponent kitabxanası seçmək** — UI guidelines gələnə qədər qadağandır.

### PR-larını adi PR kimi yoxla

Claude-un açdığı PR də eyni qaydalara tabedir: CI yaşıl, testlər var, `Task: Exx-Tnn` yazılıb,
şablonun yoxlama siyahısı doldurulub. İnsan PR-ından fərqi yoxdur — eyni gözlə bax.

## 12. Haradan soruşmaq

- Qayda ilə bağlı sual → əvvəlcə `CLAUDE.md`, sonra repo sahibi
- Task-ın scope-u aydın deyilsə → həmin issue-da şərh yaz
- Təhlükəsizlik zəifliyi → **public issue açma**, private vulnerability reporting işlət
