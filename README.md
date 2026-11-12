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
/scripts                bootstrap-github.sh və digər əməliyyat skriptləri
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

## Vəziyyət

Repozitoriya **skeleton** mərhələsindədir: qovluq quruluşu qurulub, kod hələ yazılmayıb.
İş `CLAUDE.md`-dəki epic sırası ilə gedir (E00 → E20); hər PR bir task-ı bağlayır, hər task bir epic-ə aiddir.

## Toxunulmaz qaydalar (qısa)

- **Heç vaxt hard delete yoxdur** — nə MongoDB, nə PostgreSQL, nə obyekt saxlamada. Yalnız soft delete.
- Pul həmişə `long` **qəpik**, yalnız serverdə hesablanır.
- Store-lar arası effektlər yalnız **outbox event**-lərlə; consumer-lər idempotentdir.
- Kateqoriya kodu/adı üzrə şərt yazılmır — bütün davranış kateqoriya sənədindən gəlir.
- Rəng və şriftlər yalnız `/design-tokens`-dan; komponentdə hard-code hex yoxdur.
- `docs/dependencies/allowlist.json`-da olmayan heç bir paket əlavə edilmir (ADR + təsdiq tələb olunur).
