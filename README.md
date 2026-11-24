# QONAQET

Azərbaycan üçün açıq **qonaq etmə və hədiyyə platforması**. Başlanğıc nöqtəsi yemək və içkidir, lakin
bütün əsas anlayışlar (`catalogItems`, `businesses`, `Voucher`) genericdir — platforma sxem dəyişikliyi
olmadan çiçək, kitab və xidmətlərə qədər genişlənə bilər.

Üç sütun, bir backend:

1. **Hədiyyə** — konkret insana hədiyyə (açıq və ya anonim, yerində və ya uzaqdan).
2. **İsmarla** — tanımadığın insanlara kateqoriya üzrə qonaqlıq (askıda, hovuz, kampaniya).
3. **Biznes paneli** — QR ilə təhvil, kataloq, satış hesabatları, balans və ödənişlər, işçilər.

> **Yeganə həqiqət mənbəyi: [`CLAUDE.md`](./CLAUDE.md).** Arxitektura, texnoloji stack, domen modeli,
> biznes qaydaları, asılılıq siyasəti və iş prosesi orada təsvir olunur. Hər hansı qərar o faylla
> ziddiyyət təşkil edirsə, `docs/adr/` altında ADR tələb olunur.

## Repozitoriya quruluşu

```
/backend                .NET 10 solution — Clean Architecture + CQRS
  src/BuildingBlocks/Qonaqet.SharedKernel    Result<T>, Error, Money (qəpik), Clock (Bakı), Entity, DomainEvent
  src/Qonaqet.Domain                         təmiz domen (xarici paket yoxdur)
  src/Qonaqet.Application                    use-case-lər (Features/<Area>/Commands|Queries|Events|Jobs)
  src/Qonaqet.Contracts                      versiyalanmış integration event-lər və webhook DTO-ları
  src/Qonaqet.Infrastructure                 Mongo, Postgres, Valkey, RabbitMQ, S3 (RustFS), Payriff, SMTP, FCM
  src/Qonaqet.Api                            mobil + veb üçün public API
  src/Qonaqet.Integration.Api                partnyor API (/partner/v1)
  src/Qonaqet.Worker                         öz scheduler-i, outbox relay, consumer-lər, webhook dispatcher
  tests/                                     Domain, Application, Integration (Testcontainers), Architecture
/mobile                 Flutter (user mode + business mode), feature-first clean
  lib/features/<feature>/{data,domain,presentation}
  packages/             öz plugin-lərimiz: secure_store, code_scanner, share_sheet
/web                    Angular CLI multi-project workspace (Nx YOX)
  projects/landing           public sayt (SSR) + /g/:token, /reytinq, /kampaniya/:slug, /developers
  projects/business-portal   xidmət təchizatçıları: kod yoxla/təhvil ver, kataloq, satış, ödənişlər, inteqrasiyalar
  projects/admin             admin + korporativ portal
  projects/{ui,design-tokens,api-client,auth,util}
  tools/openapi-gen          öz, asılılıqsız TypeScript client generatoru
/design-tokens          tokens.json → Style Dictionary → CSS vars (web) + Dart theme (mobile)
/docs                   ADR-lər (docs/adr/NNNN-title.md), epic-lər, inteqrasiya təlimatı, diaqramlar
/deploy                 docker-compose (dev), Dockerfile-lar, env şablonları
/scripts                epic.py (claim/status), board.py, repo-settings.py, CI skriptləri
/.github                workflow-lar (path filter ilə), issue/PR şablonları
```

## Dev mühiti

Bütün infrastruktur (MongoDB replica set, PostgreSQL, Valkey, RabbitMQ, RustFS + versioning-li bucket,
Mailpit, Aspire Dashboard) bir əmrlə qalxır — `.env` faylı tələb olunmur:

```bash
docker compose -f deploy/docker-compose.yml up -d --wait
```

| Servis | Ünvan |
|---|---|
| MongoDB (`rs0`) | `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true` |
| PostgreSQL | `localhost:5432` (`qonaqet` / `qonaqet-dev-only`) |
| Valkey | `localhost:6379` |
| RabbitMQ | `localhost:5672`, panel http://localhost:15672 |
| RustFS (S3 API) | http://localhost:9000, konsol http://localhost:9001 |
| Mailpit | SMTP `localhost:1025`, məktublar http://localhost:8025 |
| Aspire Dashboard | http://localhost:18888, OTLP gRPC `localhost:4317` |

- Portları və dev parollarını dəyişmək üçün: `cp deploy/env/.env.example deploy/env/.env`, sonra
  `--env-file deploy/env/.env` əlavə et. Real sirlər repo-ya yazılmır.
- Backend host-ları və veb app-lar `apps` profilindədir (`--profile apps up -d --build`); onların
  Dockerfile-ları müvafiq skeleton task-ları ilə gəlir.
- Yoxlama: `python3 -m unittest discover -s deploy/tests` (statik) və `deploy/tests/smoke.sh` (real stack).

### Giriş (dev)

Api host-da giriş email OTP ilədir: `POST /api/v1/auth/otp/send` → `POST /api/v1/auth/otp/verify`
(cavabda `tokens.accessToken` 15 dəq, `tokens.refreshToken` 30 gün, hər `POST /api/v1/auth/refresh`-də rotasiya
olunur). Kod email ilə gəlir: dev-də məktubu Mailpit-də (http://localhost:8025) görürsünüz; Development-də kod
əlavə olaraq Api logunda `DEV giriş kodu` sətrindədir (digər mühitlərdə heç vaxt loglanmır).
Sirlər: `Jwt__SigningKey` (≥ 32 bayt) və `Security__TokenHashSecret`; Development-də verilməsə dev açarları işlənir,
digər mühitlərdə məcburidir.

### Fayllar, email, PDF, push (E03)

- **Fayllar:** `POST /api/v1/files` (multipart `file` + `purpose`), `GET`/`DELETE /api/v1/files/{id}`. Fayllara yalnız
  `fileId` ilə istinad olunur; RustFS-də açarlar dəyişməzdir, versioning açıqdır, heç nə silinmir (`DELETE` = soft delete → 404).
  Konfiqurasiya: `Storage__ServiceUrl`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__Bucket`.
- **Email:** `Smtp__Host`, `Smtp__Port`, `Smtp__Security` (`None`/`StartTls`/`SslOnConnect`), `Smtp__UserName`, `Smtp__Password`,
  `Smtp__FromAddress`. Şablonlar `Infrastructure/Email/Templates`, rənglər `design-tokens/tokens.json`-dan.
- **PDF:** yalnız Worker-də (Playwright + headless Chromium; Worker image-i `--target worker` stage-indən qurulur).
  Lokal testlər Chromium-u `PLAYWRIGHT_BROWSERS_PATH`-a özü yükləyir.
- **Push:** `Firebase__CredentialsJson` (service account JSON) verilməsə push göndərilmir (dev). İstifadəçi bildiriş
  növlərini `GET`/`PATCH /api/v1/me/notifications` ilə push/email üzrə söndürə bilir.

### Bizneslər və kataloq (E04)

- **Müraciət:** `POST /api/v1/businesses/apply` → `Pending`; admin `POST /api/v1/admin/businesses/{id}/activate` edəndə
  müraciətçi `BusinessOwner` olur (`suspend`, `DELETE` = kaskad soft delete, `restore`).
- **Panel:** biznes həmişə URL-dədir — `/api/v1/business/{businessId}/…` (`staff`, `catalog`, `settings`); giriş
  `businessMemberships`-dən yoxlanılır, başqa biznes `403 business_access_denied`. Şəbəkə sahibi bizneslərini
  `GET /api/v1/business/mine` ilə görür.
- **İctimai:** `GET /api/v1/businesses?lat=&lng=&radiusKm=&type=&acceptsPool=&acceptsNetworkVouchers=` (2dsphere, yalnız aktivlər)
  və `GET /api/v1/businesses/{idOrSlug}` (aktiv kataloq ilə).

### Kateqoriyalar və təsdiq (E05)

- **Kateqoriyalar datadır:** `GET /api/v1/categories` (anonim) — mobil/veb sihirbazı, filtrləri bundan qurur. Admin
  `/api/v1/admin/categories` ilə yaradır/redaktə edir (deaktivasiya = `isActive: false`); yaradılanda default hovuz yaranır.
  İstifadədə olan kateqoriyanın kodu dəyişmir, aktiv təklif/kampaniya/hovuz balansı/etibarlı təsdiqi olan kateqoriya silinmir
  (`409 category_in_use`). Startup 4 kateqoriyanı (`everyone`, `student`, `veteran-family`, `in-need`) idempotent seed edir.
- **Təsdiq:** `POST /api/v1/verification` (multipart: `categoryId`, `subtype`, `fields` JSON, `fin`, `files[]`), `GET /api/v1/verification/me`.
  FIN yalnız `HMAC-SHA256(Security__FinHashSecret, UPPER(FIN))` kimi saxlanılır, bir kateqoriyada bir hesaba bağlanır
  (`identity_already_used`). Admin `/api/v1/admin/verifications` ilə təsdiqləyir/rədd edir; `validUntil` kateqoriyanın qaydasından
  (Bakı təqvimi) hesablanır və əl ilə dəyişdirilə bilər. Müddət məntiqidir; Worker bitməyə 14 gün qalmış bir dəfə push + email göndərir.

### Sosial qonaqlıq (E06)

- **Təkliflər:** sahib `POST /api/v1/business/{id}/offers` ilə gündəlik pulsuz təklif (kvota, istəyə bağlı `HH:mm` pəncərəsi), işçi
  `POST .../offers/suspended` ilə kassadakı "askıda"nı (say, adlı/anonim ianəçi, istəyə bağlı `donorEmail`) qeyd edir. İctimai siyahı
  `GET /api/v1/offers?categoryId=&businessId=`.
- **Claim:** `POST /api/v1/offers/{id}/claim` 30 dəqiqəlik rezerv yaradır. Vahid tək şərtli atomik `FindOneAndUpdate` ilə götürülür
  (tranzaksiyasız, `INonTransactionalCommand`), gündəlik sayğac Bakı günü ilə lazy sıfırlanır, bitəndə `409 quota_exhausted`. Limitlər:
  bir aktiv rezerv, təklifdən gündə bir, kateqoriyalar arasında ən səxavətli gündəlik/aylıq say (`limit_reached`).
- **Kodlar:** QR `base64url("c." + claimId + "." + HMAC)` (`Security__QrSigningSecret`) və 8 simvollu qısa kod; DB-də yalnız qısa kodun
  hash-i saxlanılır. `GET /api/v1/me/claims`, `POST /api/v1/claims/{id}/cancel`, `GET /api/v1/me/donations` (yalnız aqreqatlar).
- **Təhvil:** `POST /api/v1/business/{id}/redeem/preview` və `.../redeem` (`{ code }`, rate-limitli); işçi yalnız məhsulu, statusu,
  məbləği və etiketi görür (həssas kateqoriyada "Qonaq"). Təhvil `ClaimRedeemed` outbox hadisəsi yazır; gündəlik/askıda üçün komissiya yoxdur.
- Worker `claim-expiry` (dəqiqədə bir) bitmiş rezervləri buraxır; `social-claims` consumer-i `UserBlocked` və `BusinessDeleted`-də aktiv
  claim-ləri ləğv edir, biznesin təkliflərini kaskad soft delete/bərpa edir.

### Ödənişlər və ledger (E07)

- **Provayder:** Payriff v3 (`Payriff__AppKey`, `Authorization` başlığında prefikssiz). Development-də açar boşdursa (və ya
  `Payments__Provider=Fake`) Fake gateway işləyir: ödəniş səhifəsi `GET /api/v1/payments/fake/{orderId}` (`?result=paid|failed`), yalnız
  Development-də açıqdır. Sandbox partnyorlar həmişə Fake-dən istifadə edir.
- **Ödəniş:** `POST payments` yoxdur — məqsəd əmrləri (hədiyyə, ianə …) `PaymentStarter` ilə serverdə hesablanmış məbləğlə ödəniş başladır;
  tətbiq `paymentUrl`-i açır və `GET /api/v1/payments/{id}`-ni sorğulayır. Callback `POST /api/v1/payments/payriff/callback` imzasızdır: yalnız
  tetikleyicidir, status həmişə Get Order ilə təsdiqlənir. Worker `payment-recheck` (5 dəqiqə) 2 dəqiqədən köhnə `Pending`-ləri yoxlayır,
  30 dəqiqədən köhnə ödənilməmişləri `Expired` edir. Təsdiq `PaymentConfirmed` outbox hadisəsi ilə eyni Postgres tranzaksiyasındadır.
- **Refund:** `POST /api/v1/admin/payments/{id}/refunds` (tam və ya qismən, `Idempotency-Key`); cəm heç vaxt ödənişi aşmır.
- **Ledger:** `ledger_entries` append-only, `source_event_id` unikal (consumer idempotentdir). Onlayn ödənişli təhvil → `RedemptionCredit` +
  `Commission` (`Ledger__CommissionPercent`, default 7); gündəlik/askıda üçün yazı yoxdur. Biznes balansı `SUM(amount)`:
  `GET /api/v1/business/{id}/ledger`, `GET .../payouts` (yalnız sahib).
- **Payout və uzlaşdırma:** admin `POST /api/v1/admin/payouts` (bank köçürməsi → `Payout` ledger yazısı, balansı aşa bilməz); Worker
  `ledger-reconciliation` gecəlik hovuzların Mongo balansını (`balance − reserved`) ledger ilə müqayisə edir, fərqlər
  `GET /api/v1/admin/ledger/reconciliation`-da görünür və xəbərdarlıq logu verir.

### Hovuzlar, kampaniyalar və ianələr (E08)

- **İanələr** (`Idempotency-Key`, məbləğ serverdə; cavabda `refundable: false` — ianələr geri qaytarılmır): `POST /api/v1/donations/remote`
  (kataloq məhsulu × say → ödənişdən sonra görünən `Remote` təklif), `POST .../donations/pool` (kateqoriyanın default hovuzu),
  `POST .../donations/campaign`. Ödəniş təsdiqini Worker-in `payment-fulfillment` consumer-i idempotent yerinə yetirir (hovuz balansı +
  ledger `PoolDeposit`). `GET /api/v1/me/donations/pools` — ianəçinin aqreqatları.
- **Hovuz claim-i:** `POST /api/v1/pool-claims` (`acceptsPool` biznesin məhsulu; `poolAccess` olan kateqoriyalar; əvvəl ən tez bitən aktiv
  kampaniya hovuzu, sonra default). Rezerv atomik şərtli yeniləmədir (`balance − reserved ≥ qiymət`), `pool_insufficient`; ledger-də
  `PoolReserve`/`PoolRelease`, beləliklə hovuz hesabı = `balance − reserved`. Alıcı "Leyla sizə ısmarladı 💚" görür: claim ən köhnə
  istifadə olunmamış ianədən maliyyələşir, təhvildə məbləğ ianələrdən FIFO ilə xərclənir.
- **Kampaniyalar:** admin `/api/v1/admin/campaigns` (yarat, dəyiş, aktivləşdir — hovuz yaranır); 4 qaralama şablon seed olunur. Worker
  `campaign-end` (5 dəqiqə) bitmiş kampaniyanı `Ended` edir və qalığı default hovuza köçürür (`CampaignRollover`).
- **İctimai:** `GET /api/v1/public/stats` (60 s keş), `GET /api/v1/pools/summary`, `GET /api/v1/campaigns/active` — yalnız aqreqatlar.

### Hədiyyələr (E09)

- **Yaratma:** `POST /api/v1/gifts` (`Idempotency-Key`): məhsullar (bir biznesin 1–10 `isGiftable` vahidi), biznes vauçeri
  (`voucherMinQepik`–`voucherMaxQepik`) və ya şəbəkə vauçeri (2–200 AZN); qiymət yalnız serverdə. Alıcı email və ya "linki özüm göndərəcəm",
  tema, mesaj, adlı/anonim, `deliverAt` (≤90 gün), `expiryAction`. Ödənişdən sonra `Scheduled` + link token-i; göndərən linki bir dəfə
  `POST .../gifts/{id}/share-link` ilə götürür, `reissue-link` köhnəni etibarsız edir. Token yalnız hash kimi (+ müvəqqəti şifrəli nüsxə,
  ADR 0007) saxlanılır, log və trace-lərdə maskalanır.
- **Çatdırılma və açılış:** Worker `gift-delivery` (dəqiqədə bir) temalı email + push; `GET /api/v1/public/gifts/{token}` (anonim,
  rate-limitli) kartı, kodları və biznes(lər)i göstərir, ilk açılış göndərənə bildirilir; `POST .../public/gifts/{token}/claim` hədiyyəni
  hesaba bağlayır (link işləməyə davam edir). `GET .../gifts/sent|received|{id}`.
- **Təhvil:** `POST /api/v1/business/{id}/redeem[/preview]` hədiyyə kodlarını da qəbul edir (`lines` və ya `amountQepik`, qismən);
  bir Mongo tranzaksiyası + optimistik `version`, toqquşmada bir təkrar, sonra `gift_changed`. Ledger: təhvil verən biznesə kredit − 7%.
- **Ləğv və müddət:** `POST .../gifts/{id}/cancel` (yalnız `Scheduled`, tam refund, idempotent); Worker `gift-expiry` (15 dəqiqə) 7 gün
  qalmış xatırladır, sonra qalığı refund edir və ya hovuza bağışlayır (`GiftExpiryDonation`); `gift-refunds` gözləyən refundları təkrarlayır.

### Korporativ hədiyyələr və CSR (E10)

- **Şirkətlər:** admin `/api/v1/admin/corporates` (yarat, dəyiş) və `POST .../corporates/{id}/users {email}` — istifadəçi şirkətə bağlanır,
  `CorporateUser` rolu alır. Portal: `GET /api/v1/corporate/me`.
- **Sifariş:** `POST /api/v1/corporate/orders` (şablon: məhsullar / biznes vauçeri / şəbəkə vauçeri, tema `Corporate`, qiymət serverdə),
  `POST .../orders/{id}/recipients` (CSV `name,email`, ≤ 5,000 sətir, sətir-sətir xəta hesabatı), məbləğ = qiymət × alıcı sayı.
- **Ödəniş:** `POST .../orders/{id}/pay` (`Idempotency-Key`): `Card` (Payriff) və ya `BankTransfer` (yalnız "hovuza bağışla" ilə); admin
  `POST /api/v1/admin/corporate-orders/{id}/confirm-bank-transfer` bank referansı ilə təsdiqləyir (audit).
- **Fan-out:** Worker `corporate-fanout` (dəqiqədə bir) hər alıcıya bir hədiyyə yaradır, 200-lük paketlər, dublikatsız, yarıda qalan yerdən davam edir.
- **Hesabatlar:** `GET .../orders/{id}/report[.csv]` (çatdırılan/açılan/təhvil verilən, istifadə olunmamış dəyər, alıcı üzrə yalnız "təhvil verildi:
  bəli/xeyr"); CSR: `GET .../corporate/campaigns`, `.../campaigns/{id}/report[.csv]` (yalnız aqreqatlar). PDF: `POST ...report.pdf` → Worker
  (`corporate-reports`, Playwright) → `GET .../corporate/reports/{id}/file`.

### Reytinq (E11)

- **Xal yalnız təhvildə:** score consumer-i `GiftRedeemed`/`ClaimRedeemed`-dən `score_events` (append-only) yazır; dəyərlər
  `Leaderboard__Points__*` (məhsul vahidi / hər başlanan 5 AZN 1, donor və biznes claim-i 2, şirkət 1/2).
- **Anti-gaming:** özünə hədiyyə və yalnız-link hədiyyə (alıcı hesaba əlavə edənə qədər) xal vermir; göndərən→alıcı cütü ayda 10, istifadəçi
  gündə 300 xal (`Leaderboard__Limits__*`, Bakı vaxtı), artığı `points = 0`; geri alma mənfi hadisə yazır.
- **Lövhələr:** `leaderboard_entries` (həftə `2026-W48`, ay `2026-11`, `all`) consumer-in tranzaksiyasında upsert olunur; Valkey
  `lb:{subjectType}:{periodType}:{periodKey}` yalnız keşdir (10 dəq. TTL, boşdursa Postgres-dən qurulur).
- **API:** `GET /api/v1/leaderboard?subjectType=&periodType=&periodKey=` (top 100 + "Sizin yeriniz"; yalnız ad və xal, opt-out/bloklanmış/aktiv
  olmayan gizlənir), `PATCH /api/v1/me/leaderboard` (opt-out, ləqəb), `GET /api/v1/me/score` (səviyyə, nişanlar, tarixçə).
- **Nişanlar:** Worker `leaderboard-badges` (saatlıq) bitmiş Bakı həftəsinin ilk 3-ü və ayın ilk 10-u üçün, idempotent (`badge_awards`).
- **Yenidən hesablama:** admin `POST /api/v1/admin/leaderboard/recompute {subjectType, periodType, periodKey}` — `score_events`-dən, yalnız fərqli
  sətirlər dəyişir, audit log-a düşür.

## Vəziyyət

Backend təməli (E01), hesab/giriş (E02: OTP, JWT + refresh rotasiyası, rollar, profil, FCM token-ləri, bloklama) və
fayllar/email/bildirişlər (E03: RustFS saxlama, yükləmə axını, MailKit + şablonlar, Playwright PDF, FCM push + seçimlər) və
bizneslər/kataloq (E04: müraciət + aktivləşdirmə, işçilər, kataloq, parametrlər, coğrafi axtarış, şəbəkələr) və
kateqoriyalar/təsdiq (E05: dinamik kateqoriyalar + default hovuz, seed, təsdiq sihirbazı, FIN hash, admin baxışı, xatırlatmalar) və
sosial qonaqlıq (E06: gündəlik/askıda təkliflər, atomik claim, limitlər, QR/qısa kod, təhvil, müddət job-u, məxfilik testləri) və
ödənişlər/ledger (E07: Payriff + Fake gateway, payments/refunds, callback + yenidən yoxlama, ledger + komissiya, payout-lar, uzlaşdırma) və
hovuzlar/kampaniyalar (E08: remote/hovuz/kampaniya ianələri, hovuz claim-ləri, donor atribusiyası, kampaniya qalığının köçməsi, ictimai statistika) və
hədiyyələr (E09: məhsul/vauçer/şəbəkə hədiyyələri, ödəniş, link token-ləri, ictimai səhifə, çatdırılma, qismən təhvil, ləğv, müddət bitməsi) və
korporativ (E10: şirkətlər, toplu sifarişlər + CSV, kart/bank ödənişi, fan-out, PDF/CSV hesabatlar, CSR) və
reytinq (E11: təhvildə xal, anti-gaming, Postgres aqreqatları + Valkey, lövhə API-si, səviyyələr/nişanlar, yenidən hesablama) hazırdır.
İş `docs/epics/*.md`-də izlənir; ümumi görünüş **[`docs/BOARD.md`](docs/BOARD.md)**. Hər PR bir task-a aiddir (`Task: Exx-Tnn`),
hər developer eyni anda bir epic saxlayır (`scripts/epic.py status`).

## Toxunulmaz qaydalar (qısa)

- **Heç vaxt hard delete yoxdur** — nə MongoDB, nə PostgreSQL, nə obyekt saxlamada. Yalnız soft delete.
- Pul həmişə `long` **qəpik**, yalnız serverdə hesablanır.
- Store-lar arası effektlər yalnız **outbox event**-lərlə; consumer-lər idempotentdir.
- Kateqoriya kodu/adı üzrə şərt yazılmır — bütün davranış kateqoriya sənədindən gəlir.
- Rəng və şriftlər yalnız `/design-tokens`-dan; komponentdə hard-code hex yoxdur.
- `docs/dependencies/allowlist.json`-da olmayan heç bir paket əlavə edilmir (ADR + təsdiq tələb olunur).
