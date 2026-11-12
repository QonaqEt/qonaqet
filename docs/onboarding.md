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

## 10. Haradan soruşmaq

- Qayda ilə bağlı sual → əvvəlcə `CLAUDE.md`, sonra repo sahibi
- Task-ın scope-u aydın deyilsə → həmin issue-da şərh yaz
- Təhlükəsizlik zəifliyi → **public issue açma**, private vulnerability reporting işlət
