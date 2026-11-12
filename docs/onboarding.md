# Komandaya qoşulma

QONAQET-ə xoş gəldin. Bu sənəd ilk günün yol xəritəsidir: nəyə giriş lazımdır, nə oxumalısan,
iş necə gedir və hansı qaydalar danışılmazdır.

> **Vəziyyət (2026-11-12):** repo **skeleton** mərhələsindədir. Qovluq quruluşu, sənədlər və
> GitHub avtomatlaşdırması hazırdır; **məhsul kodu hələ yazılmayıb**. `E00` epic-i işləyir.
> Aşağıda "hələ yoxdur" yazılan şeylər `E00` tamamlananda görünəcək.

---

## 1. Giriş icazələri

- [ ] **GitHub org dəvəti** — `QonaqEt` təşkilatı
- [ ] **Repo girişi** — [`QonaqEt/qonaqet`](https://github.com/QonaqEt/qonaqet) (private)
- [ ] **Board girişi** — "QONAQET" Project (v2)
- [ ] **2FA aktivdir** (org tələbidir)
- [ ] Əgər review məsuliyyəti daşıyırsansa — `@QonaqEt/owners` komandasına əlavə olunmaq

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

**Epic → task → PR.** Hər task bir epic-in sub-issue-sudur, hər PR bir task bağlayır.

Board sütunları: `Backlog → Ready → In Progress → In Review → Done`

1. **Task götür** — cari epic-in `Ready` sütunundakı ən yuxarıdakı task. Cari epic-in açıq
   task-ları varkən yeni epic-ə keçmə (istisna: `parallel-ok` etiketli task-lar).
2. **`In Progress`-ə keçir** (board özü də edir — branch push edəndə).
3. **Branch:** `feat/<issue#>-<slug>` · `fix/...` · `chore/...`
4. **Testlərlə yaz.** Test olmadan PR açma.
5. **Commit:** Conventional Commits, issue nömrəsi ilə
   `feat(gifts): partial voucher redemption (#123)`
6. **PR:** şablonu doldur, `Closes #123` yaz. UI dəyişikliyi varsa skrinşot məcburidir.
7. **CI yaşıl olmalıdır:** build, testlər, lint, arxitektura testləri.
8. **Squash merge.** Task avtomatik `Done`-a keçir.

Epic-in bütün task-ları bitəndə: acceptance criteria yoxlanılır, `README.md`/`docs/` yenilənir,
epic bağlanır, `epic-Exx-done` tag-ı yaradılır.

**Yeni iş çıxırsa** — düzgün epic altında yeni task yarat. Scope-u səssizcə genişləndirmə.

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

GitHub Projects (v2) **yalnız GraphQL** ilə idarə olunur və Actions-ın avtomatik verdiyi
`GITHUB_TOKEN` ona **yaza bilmir**. Ona görə board-a toxunan hər workflow ayrıca token tələb edir:
`bootstrap.yml`, `project-sync.yml`, `epic-progress.yml`.

### Addımlar

1. https://github.com/settings/tokens → **Generate new token (classic)**
2. **Note:** `QONAQET project automation` · **Expiration:** 90 gün
3. Scope-ları işarələ — **üçü də**:

   | Scope | Siyahıda harada | Nə üçün |
   |---|---|---|
   | `repo` | ən yuxarıda, birinci | issue, label, sub-issue |
   | `read:org` | `admin:org`-un altında | org və project oxumaq |
   | `project` | aşağıda, ayrıca blok | **board yaratmaq və yazmaq** |

   > ⚠️ `project` ilə `read:project` **ayrı sətirlərdir**. Yalnız alt sətri (`read:project`)
   > işarələsən, board oxunur, amma yaradılmır — workflow belə qırılır:
   > `The 'createProjectV2' field requires one of the following scopes: ['project']`.
   > **Üst sətri işarələ**, alt sətir özü gəlir.

4. **Generate token** → dəyəri kopyala (bir dəfə göstərilir)
5. Repo → **Settings → Secrets and variables → Actions → New repository secret**
   - Name: `PROJECT_TOKEN`
   - Secret: kopyaladığın dəyər

### Yoxlama

**Actions → bootstrap → Run workflow.** "Project board" addımı yaşıldırsa, token işləyir.
Token yoxdursa, workflow qırılmır — həmin addımı ötürür və job summary-də bunu yazır.

### Rotasiya

- **Scope dəyişmək** (məs. `project` əlavə etmək): token-i **Edit** et → scope-u işarələ → Update.
  **Dəyər dəyişmir**, secret-i yenidən əlavə etməyə ehtiyac yoxdur.
- **Regenerate** etsən, dəyər dəyişir — `PROJECT_TOKEN` secret-ini mütləq yenilə.
- Müddəti bitməzdən əvvəl GitHub email göndərir. Bitəndə board avtomatikası səssizcə dayanır,
  ona görə xəbərdarlığı gözdən qaçırma.

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
  (`organization_projects: write` + `issues: write`) və `actions/create-github-app-token`.
  Şəxsi token olmur, avtomatik rotasiya olunur, müddəti bitmir.

## 11. Claude Code ilə işləmək

Komandada hər kəs öz Claude Code sessiyası ilə işləyir. `CLAUDE.md` repo kökündə olduğu üçün
**avtomatik yüklənir** — onu hər dəfə yapışdırmağa ehtiyac yoxdur.

### Sessiyanı başlatmaq

- **Bulud:** claude.ai/code → yeni sessiya → mənbə repo kimi `QonaqEt/qonaqet` seç
- **Lokal:** repo kökündə `claude` əmrini işlət

Hər iki halda Claude `CLAUDE.md`-ni, `docs/epics/` və `docs/adr/` qovluqlarını oxuya bilir.

### İlk mesaj

Nə üzərində işlədiyini və hansı task-ı götürdüyünü de. Məsələn:

```
E01-T04 task-ı üzərində işləyirik (#21): öz CQRS dispatcher-imiz.
docs/epics/E01.md-dəki acceptance criteria-ya bax.
Branch aç, testlərlə yaz, Closes #21 olan PR hazırla.
```

Scope-u açıq saxlama — Claude boş yerə genişləndirməsin deyə task nömrəsini həmişə göstər.

### Bu mühitdə bilinən məhdudiyyətlər

Bulud sessiyalarında vaxt itirməmək üçün:

| Məhdudiyyət | Nə etmək |
|---|---|
| **GitHub GraphQL bloklanıb** | REST işlət. `gh issue close` GraphQL-dir — əvəzinə `gh api -X PATCH .../issues/N -f state=closed` |
| **Projects v2 yalnız GraphQL-dir** | Board əməliyyatları yalnız Actions runner-ində (`PROJECT_TOKEN` ilə) mümkündür |
| **`gh api --paginate` qırılır** | Link başlığı `repositories/{id}` yolunu verir. Açıq `page=N` dövrü ilə səhifələ — nümunə: `scripts/bootstrap-github.sh` |
| **Action-lar SHA ilə pinlənməlidir** | SHA tapmaq: `git ls-remote https://github.com/actions/checkout refs/tags/v5.0.0` |
| **Workflow permissions read-only-dir** | Hər workflow öz `permissions:` blokunu açıq yazır |
| **GitHub ikincili yaratma limiti** | Toplu issue əməliyyatlarında fasilə ver — `RATE_SLEEP` nümunəsinə bax |
| **Org adı dəyişsə, REST qırılır** | Köhnə ad `repositories/{id}`-yə yönləndirilir və bloklanır. Yeni adla yeni sessiya lazımdır |

### Nəyə icazə vermə

- **Allowlist-dən kənar paket quraşdırmaq.** `docs/dependencies/allowlist.json` yoxlanılmalıdır;
  CI onsuz da qıracaq, amma vaxt itirirsən.
- **`CLAUDE.md`-dən kənara çıxmaq** — ADR olmadan. Qaydanı dəyişmək istəyirsə, əvvəlcə ADR.
- **Scope genişləndirmək.** Yeni iş çıxırsa, yeni task açılır.
- **Angular komponent kitabxanası seçmək** — UI guidelines gələnə qədər qadağandır.

### PR-larını adi PR kimi yoxla

Claude-un açdığı PR də eyni qaydalara tabedir: CI yaşıl, testlər var, `Closes #n` yazılıb,
şablonun yoxlama siyahısı doldurulub. İnsan PR-ından fərqi yoxdur — eyni gözlə bax.

## 12. Haradan soruşmaq

- Qayda ilə bağlı sual → əvvəlcə `CLAUDE.md`, sonra repo sahibi
- Task-ın scope-u aydın deyilsə → həmin issue-da şərh yaz
- Təhlükəsizlik zəifliyi → **public issue açma**, private vulnerability reporting işlət
