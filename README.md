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
