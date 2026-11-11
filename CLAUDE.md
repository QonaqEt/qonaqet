# CLAUDE.md — QONAQET

## What this project is

**QONAQET** is an Azerbaijani open **treating and gifting platform** for everyone. Its starting point is food and drinks, but every core concept (catalog item, voucher, business type) is generic, so the platform can later expand beyond food without schema changes.

It has three pillars that share one backend:

1. **Gifting (Hədiyyə):** treat a specific person, openly or anonymously, in person or remotely.
   - buy a coffee or meal for a friend
   - send a surprise to a partner
   - send food to your mother or father
   - companies send gifts and bonuses to employees and customers
2. **Social treating (Ismarla):** treat people you don't know through categories, for example students. The donor chooses anonymous or named.
   - business daily offers
   - askıda (suspended) items
   - category pools
   - campaigns
3. **Business panel:** for cafés and other partners.
   - QR redemption
   - catalog
   - sales and redemption reports
   - balance and payouts
   - staff

### Core principles
- **Open to everyone.** Any registered user can send and receive gifts. Categories matter only for social treating.
- **Categories are dynamic data.** The admin adds new ones without a code change. Four are seeded: `everyone`, `student`, `veteran-family` (qazi / şəhid ailəsi), `in-need` (ehtiyacı olan).
- **Dignity in social treating:**
  - Donors never choose or see recipients.
  - Businesses never see recipients' identity.
  - Sensitive categories are never shown to staff.
- **Nothing is ever hard-deleted**, not in MongoDB, PostgreSQL or object storage (see "Soft delete").
- **Online payments** go through Payriff.
- Gifts are always **picked up at the business** (QR or code). Delivery/courier is not part of the MVP. Keep `fulfillmentType` (`Pickup`) on gifts so `Delivery` can be added later.

### MVP scope — ALL of the following must work
1. **Personal gifts:**
   - Pick items or a money **voucher** at one business, or a **network voucher** usable at any participating business.
   - Choose the recipient by email, or by a share link that the sender sends via WhatsApp/Telegram.
   - Add an occasion theme and a message.
   - Choose a named or anonymous sender.
   - Send immediately or schedule a delivery time.
   - Pay online.
2. **Corporate gifts and bonuses:**
   - The company uploads a recipient list (CSV of name + email) and picks one gift template (items, voucher or network voucher), a message and its branding.
   - Payment is by card, or by bank transfer confirmed by the admin.
3. **Social treating:**
   - Dynamic categories with verification.
   - Ways to treat:
     - `BusinessDaily` offers
     - `Suspended` (askıda at the counter)
     - `Remote` purchases for a category
     - category pools
     - campaigns
   - For anything they pay for, the donor chooses **anonymous or named** display.
4. **Business panel:**
   - Web, plus a business mode in the mobile app.
   - Features: QR/code redemption (including partial voucher redemption), catalog, offers, sales of gifts, redemption history, ledger, payouts, staff, CSV export.
5. **Corporate CSR campaigns** with PDF/CSV reports.
6. **QONAQET Plus:** a coupon hub from brand partners.
7. **Monetization:**
   - A configurable commission (default 7%) on every **online-paid** redemption: gifts, remote, pool, campaign. It is deducted from the business payout.
   - In-person `BusinessDaily` and `Suspended` items carry no commission.
8. Abuse prevention:
   - limits
   - one-time or HMAC-signed QR codes
   - a FIN hash for verified categories
   - reports and moderation
9. **Leaderboard ("Reytinq"):** a friendly competition. Whoever treats the most people is at the top. There are separate boards for people, businesses and companies (weekly / monthly / all-time), plus levels and badges.
10. **Landing website**: youthful, animated, carrying the feeling of solidarity, plus the public gift page.
11. **Business portal**: a separate Angular app for service providers (code/coupon verification, redemption and management).
12. **Integration platform**: a partner API + webhooks, so restaurants, POS systems, delivery apps and other services can integrate easily.
13. City: Baku. UI language: Azerbaijani (structured for `en`/`ru` later).

---

## Architecture overview

```
                    ┌──────────────────────── Clients ────────────────────────┐
                    │ Flutter app │ Landing (SSR) │ Business portal │ Admin/Corp │
                    └──────┬──────┴──────┬────────┴───────┬─────────┴─────┬──────┘
                           │  HTTPS/JSON (JWT)            │               │
 Partner systems ──►  Qonaqet.Integration.Api       Qonaqet.Api  ◄────────┘
 (POS, restaurants,   (OAuth2 client credentials,   (public app API)
  delivery apps,       webhooks out)                       │
  banks)                     │                             │
                             └────────────┬────────────────┘
                                          ▼
                     Application (CQRS: commands / queries / handlers)
                                          ▼
                Domain  ◄──  Infrastructure (Mongo, Postgres, Valkey, S3 storage,
                                             RabbitMQ, Payriff, SMTP, FCM)
                                          ▲
                                   Qonaqet.Worker
                 (own scheduler jobs, outbox relay, consumers, webhooks)
```

- **Modular, clean, CQRS** backend. One codebase with three hosts:
  - `Api`: mobile + web
  - `Integration.Api`: partners
  - `Worker`: background jobs and consumers
- All three hosts share Domain, Application and Infrastructure.
- **Data stores, each with a clear role:**

| Store | What lives there | Why |
|---|---|---|
| **MongoDB** (replica set) | Operational documents: users, categories, verifications, businesses, catalog, offers, claims, gifts, gift redemptions, pools, campaigns, corporates, corporate orders, Plus, reports, files metadata, OTPs | Flexible documents (dynamic categories, catalog), geo queries |
| **PostgreSQL** | **Financial and append-only records:** payments, refunds, ledger entries, payouts, partner balances, score events, leaderboard aggregates, audit logs, integration clients, webhook subscriptions/deliveries, job run history | ACID, strict constraints, reporting/SQL |
| **Valkey** (Redis-compatible) | Derived/ephemeral data only: cache, rate limiting, distributed locks, idempotency-key responses, leaderboard sorted sets (rebuildable from Postgres) | Speed. Never a source of truth. |
| **S3-compatible object storage**: **RustFS** (dev, staging, production single-node); fallback: Ceph RGW / managed S3 | Files (immutable keys, versioning on, never removed) | Standard API, vendor-neutral |
| **RabbitMQ** | Integration/domain events between hosts | Decoupling, retries |

- **Cross-store consistency** uses the **transactional outbox** + **idempotent inbox**:
  - Each store has its own outbox: the Mongo `outboxMessages` collection and the Postgres `outbox_messages` table. The event is written in the same transaction as the state change.
  - The Worker relays outbox messages to RabbitMQ.
  - Consumers record each processed `messageId` in a Postgres `inbox_messages` table (unique) and skip duplicates.
  - Example: a gift redemption commits in Mongo → `GiftRedeemed` event → the consumer writes the Postgres ledger, score events and webhooks.
  - The ledger is therefore eventually consistent (seconds). Balances shown to businesses come from Postgres.

### Decisions that change earlier rules
- `payments`, `ledgerEntries`, `payouts`, `scoreEvents`, `leaderboardEntries` and `auditLogs` live in **PostgreSQL** (snake_case tables). They are not Mongo collections.
- Leaderboard reads come from Valkey sorted sets (`lb:{subjectType}:{periodType}:{periodKey}`), updated by the score consumer. If the cache is empty, the API rebuilds from Postgres.
- Background jobs run in the **own scheduler** in the Worker (`BackgroundService` + Postgres advisory locks + `job_runs` table). Each job is still safe to run concurrently (atomic status transitions).
- The soft delete rule applies to **MongoDB, PostgreSQL and object storage**. Valkey keys are infrastructure, not business records, so they may expire.

---

## Repository layout (monorepo)

```
/backend                .NET solution (see "Backend structure")
/mobile                 Flutter app (user mode + business mode)
/web                    Angular CLI multi-project workspace (no Nx)
  projects/landing          Public website (SSR): marketing, animations, public gift page /g/:token, partner/corporate forms
  projects/business-portal  Service-provider portal: code verification/redemption, catalog, sales, payouts, staff, integrations
  projects/admin            Admin + corporate portal (role-based)
  projects/ui           Shared UI library (per the UI guidelines, when provided)
  projects/design-tokens Generated CSS variables from /design-tokens
  projects/api-client   TypeScript client generated by our own tools/openapi-gen script
  projects/auth, projects/util
  tools/openapi-gen     own dependency-free generator
/design-tokens          tokens.json (single source) → Style Dictionary → CSS vars (web) + Dart theme (mobile)
/docs                   ADRs (docs/adr/NNNN-title.md), integration guide, diagrams
/deploy                 docker-compose (dev), Dockerfiles, env templates
/.github                workflows (CI per area with path filters), issue templates (epic, task, bug), PR template
CLAUDE.md
README.md
```

## Dependency policy (mandatory)

**Goal: no dependency that can become a security, licensing or abandonment risk.**

1. **Allowed sources only:**
   - **Platform vendors:** Microsoft (.NET, ASP.NET Core, EF Core, Playwright), Google (Angular, Flutter, Dart, Firebase), Amazon (AWS SDK), Apache Software Foundation, Linux Foundation / CNCF (OpenTelemetry, Valkey), the .NET Foundation.
   - **Official vendor SDKs of a product we run:** MongoDB Inc. (MongoDB.Driver), Npgsql (the PostgreSQL provider used by Microsoft), StackExchange.Redis (used by Microsoft's own Redis packages), RabbitMQ team (RabbitMQ.Client), Docker Inc. (Testcontainers).
   - On pub.dev: only packages from the **verified publishers** `dart.dev`, `flutter.dev`, `google.dev`, `firebase.google.com`.
   - On npm: only `@angular/*`, `@angular-devkit/*`, `@schematics/angular`, `rxjs`, `typescript`, `zone.js` (if needed), `@playwright/test`, `echarts` (Apache), `lottie-web` (Airbnb), `style-dictionary` (Amazon), and the Angular CLI default test runner.
2. **Build it ourselves before adding a library.** Mediator/CQRS dispatcher, mapping, validation, CSV, templating, scheduler, rate limiter, QR encoder, architecture tests, test data builders: all of these are **our own small code** in the repo.
3. **Forbidden:**
   - anything commercial or "free below X revenue": MediatR 13+, AutoMapper 15+, FluentAssertions 8+, MassTransit 9+, QuestPDF, Hangfire Pro, Duende
   - single-maintainer or abandoned packages
   - packages with GPL/AGPL/SSPL licenses linked into our code
   - Nx (a supply-chain incident in 2025)
   - MinIO (archived in Feb 2026)
4. **Allowlist file:** `docs/dependencies/allowlist.json` lists every package with its publisher, license and reason.
   - The CI job `dependency-guard` fails when a lock file (`packages.lock.json`, `package-lock.json`, `pubspec.lock`) contains a package that is not on the list.
   - Adding a package requires an ADR plus the `dependency-approved` label from the repo owner.
5. Versions are pinned via lock files. Dependabot proposes updates. GitHub Actions are pinned by **commit SHA**.

## Tech stack (fixed — do not substitute)

### Backend — .NET 10 (LTS)
| Concern | Choice | Source |
|---|---|---|
| Runtime | **.NET 10**, ASP.NET Core (controllers), C# 14 | Microsoft |
| CQRS dispatcher | **Own**: `ICommand<T>`, `IQuery<T>`, handlers resolved from DI, pipeline behaviors as decorators (`SharedKernel/Messaging`) | own |
| Mapping | **Own**: explicit `ToDto()` / `ToEntity()` extension methods | own |
| Validation | **Own** `IValidator<T>` + a small rule builder (`SharedKernel/Validation`); DataAnnotations for simple DTO checks | own + Microsoft |
| Result / errors | Own `Result<T>` + `Error` | own |
| MongoDB | `MongoDB.Driver` | MongoDB Inc. |
| PostgreSQL | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` | Microsoft + Npgsql (.NET Foundation) |
| Redis-compatible cache | `StackExchange.Redis` against **Valkey** (Linux Foundation, BSD). Redis 8 also works. | Stack Exchange / LF |
| Rate limiting | ASP.NET Core `RateLimiter` middleware + **own** Redis-backed partitioned limiter (Lua script) for multi-instance | Microsoft + own |
| Messaging | `RabbitMQ.Client` + **own** outbox/inbox | RabbitMQ team |
| Background jobs | **Own scheduler**: `BackgroundService` + `PeriodicTimer`, Postgres advisory locks for single-runner jobs, a `job_runs` table for history and dashboard | Microsoft + own |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.*`; own OTP + client-credentials token endpoint | Microsoft |
| API docs | `Microsoft.AspNetCore.OpenApi` (built-in document generation); the docs UI is a static page in the landing `/developers` section | Microsoft |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience` | Microsoft |
| Object storage | `AWSSDK.S3` (S3-compatible, `ForcePathStyle = true`) behind `IFileStorage`, talking to RustFS. `InMemoryFileStorage` is used only in unit tests. | Amazon |
| Email | `MailKit` (the SMTP client Microsoft's own docs recommend) behind `IEmailSender`; **own** HTML templates with `{{placeholder}}` + HTML encoding; Mailpit only as a dev container | MS-recommended |
| Push | `FirebaseAdmin` | Google |
| Payments | Own Payriff client on `HttpClient` | own |
| PDF | HTML templates → PDF with **`Microsoft.Playwright`** (headless Chromium) in the Worker | Microsoft |
| CSV | **Own** RFC 4180 reader/writer | own |
| QR | **Own** QR encoder (ISO/IEC 18004, byte mode, ECC M). The API returns the module matrix; clients draw it, so no QR library is needed on any client. | own |
| Logging / tracing | `Microsoft.Extensions.Logging` (JSON console) + **OpenTelemetry .NET** (OTLP); dev dashboard: the standalone **Aspire Dashboard** container | Microsoft / CNCF |
| Health checks | built-in `Microsoft.Extensions.Diagnostics.HealthChecks` | Microsoft |
| Tests | **xUnit v3**, `Microsoft.AspNetCore.Mvc.Testing`, **Testcontainers** (Mongo, Postgres, Valkey, RabbitMQ); plain `Assert`; hand-written fakes and builders; **own** architecture tests (reflection + source scan) | .NET Foundation / Microsoft / Docker |

### Web — Angular (latest stable)
| Concern | Choice |
|---|---|
| Workspace | **Angular CLI multi-project workspace** (`ng new --create-application=false`), apps in `projects/` (no Nx) |
| Framework | standalone components, signals, new control flow, zoneless, `@angular/ssr` for `landing` |
| State | Angular signals + small injectable stores (own pattern); RxJS only where streams are natural |
| HTTP client | **Own generator script** (`tools/openapi-gen`, Node + TypeScript, no deps) → typed interfaces + `HttpClient` services from the OpenAPI JSON |
| Animations (landing) | **Native first**: CSS scroll-driven animations, View Transitions API, Web Animations API, `@angular/animations`; `lottie-web` (Airbnb) for illustration animations; must respect `prefers-reduced-motion` |
| UI components | **Angular CDK** + `projects/ui` (own), styled with design tokens. Final rules come from the UI colleague. |
| Charts | `echarts` (Apache) used directly through a thin own wrapper component |
| QR scanning (counter) | Native **`BarcodeDetector`** + `getUserMedia` (Chrome/Edge, the supported browsers for the counter) + USB keyboard-wedge scanners + manual input |
| i18n | `@angular/localize` (built-in) |
| Tests | Angular CLI default unit test runner; `@playwright/test` (Microsoft) e2e |
| Design tokens | `style-dictionary` (Amazon) → CSS variables + Dart theme |

### Mobile — Flutter (stable), Dart 3
| Concern | Choice |
|---|---|
| Architecture | feature-first clean (`data` / `domain` / `presentation`) |
| State / DI | Flutter built-ins: `ChangeNotifier` / `ValueNotifier` + `InheritedNotifier`; an own tiny service locator in `core/di` |
| Navigation / deep links | `go_router` (flutter.dev) + Flutter's built-in App Links / Universal Links |
| Networking | `http` (dart.dev) + own `ApiClient` (auth refresh, idempotency key, error mapping) |
| Models | `json_annotation` / `json_serializable` + `build_runner` (google.dev / dart.dev) |
| Storage | `shared_preferences` (flutter.dev); tokens in an **own platform-channel plugin** `packages/secure_store` (iOS Keychain, Android Keystore) |
| Push | `firebase_core`, `firebase_messaging` (firebase.google.com) |
| Maps | `google_maps_flutter` (flutter.dev) |
| QR scanning | `camera` (flutter.dev) + an **own plugin** `packages/code_scanner` (Android: ML Kit / Google Code Scanner via Play services; iOS: Vision/VisionKit) |
| QR display | `CustomPainter` drawing the server-provided matrix |
| Share sheet | **own plugin** `packages/share_sheet` (Android `ACTION_SEND`, iOS `UIActivityViewController`) |
| Payment page / links | `webview_flutter`, `url_launcher` (flutter.dev) |
| Animation | Flutter built-in animation APIs (implicit/explicit, `Hero`, custom painters) |
| i18n | `flutter_localizations` + `intl` (ARB) |
| Tests | `flutter_test`, `integration_test`; hand-written fakes |
| Flavors | `dev`, `staging`, `prod` |

### Infrastructure (servers; not linked into our code)
| Component | Choice | License note |
|---|---|---|
| PostgreSQL 17+ | official image | PostgreSQL License |
| MongoDB 8 Community | official image | SSPL: free for use as our app's database; we never offer MongoDB as a service |
| Valkey 8 | official image | BSD (Linux Foundation) |
| RabbitMQ 4 | official image | MPL 2.0 |
| Object storage | **RustFS** (S3-compatible, written in Rust), official image, **pinned to a GA version** (`rustfs/rustfs:1.0.x`; never `latest` or preview tags). Production: a single node with daily backups (bucket replication to a second disk/host, or a nightly sync to off-site S3). Distributed mode and preview features (e.g. MinIO on-disk compatibility) are not used. **Fallback:** Ceph RGW or a managed S3. Because the code only uses the S3 API, switching storage is a config change. | Apache 2.0. A young project (GA Sept 2026): an approved exception to the policy, re-evaluated every 6 months (ADR `0002-object-storage-rustfs`). |
| Mailpit (dev only), Aspire Dashboard (dev only) | containers | dev only |

- Docker + docker-compose for dev: api, integration-api, worker, mongo (rs), postgres, valkey, rabbitmq, rustfs (+ bucket init with versioning on), mailpit, aspire-dashboard, landing, business-portal, admin.
- Integration tests run RustFS through Testcontainers' generic container (pinned image).
- `App:PublicBaseUrl`: the public base URL (gift links, payment callbacks).

## GitHub automation

Use **only** official actions (`actions/*`, `github/*`, `docker/*`) pinned by SHA, plus the `gh` CLI (GitHub) in scripts.

### Repository settings (created by the bootstrap, documented in `docs/github-setup.md`)
- Ruleset on `main`:
  - PR required, 1 approval (owner)
  - required checks green, linear history (squash only)
  - signed commits, no force push
- Secret scanning + push protection, Dependabot alerts, private vulnerability reporting.
- Auto-delete head branches. Auto-merge enabled (used only when checks are green and the PR is approved).

### Workflows (`.github/workflows`)
| Workflow | Trigger | What it does |
|---|---|---|
| `ci-backend.yml` | PR/push, paths `backend/**` | `dotnet restore --locked-mode`, build (warnings as errors), unit + integration tests (Testcontainers), architecture tests, coverage summary |
| `ci-web.yml` | paths `web/**` | `npm ci`, lint, unit tests, build all apps, Playwright smoke |
| `ci-mobile.yml` | paths `mobile/**` | installs Flutter from the official `flutter/flutter` git tag (script, no third-party action), `flutter analyze`, tests, Android debug build |
| `dependency-guard.yml` | every PR | checks all lock files against `docs/dependencies/allowlist.json`; `actions/dependency-review-action` (blocks known CVEs and GPL/AGPL/SSPL licenses) |
| `codeql.yml` | PR + weekly | `github/codeql-action` for C#, JavaScript/TypeScript |
| `pr-checks.yml` | PR opened/edited | Conventional Commit title, `Closes #n` present, linked issue is a `task` with a parent epic; `actions/labeler` adds `area:*` labels from the paths |
| `project-sync.yml` | issues/PRs events | `actions/add-to-project` adds new issues/PRs to the "QONAQET" project. A `gh` script sets `Status` (PR opened → `In Review`; branch pushed for an issue → `In Progress`) and copies `Epic`/`Area` fields from the parent issue. |
| `epic-progress.yml` | sub-issue closed | `gh api graphql`: when all sub-issues of an epic are closed, comment a summary on the epic, move it to `Done`, close it and create the tag `epic-Exx-done` |
| `docker-publish.yml` | push to `main` | builds and pushes images to GHCR (`docker/build-push-action`), with SBOM + provenance attestations (`actions/attest-build-provenance`) |
| `release.yml` | tag `v*` | generates release notes from merged PRs (GitHub-native) |
| `bootstrap.yml` | manual (`workflow_dispatch`) | runs `scripts/bootstrap-github.sh`: labels, project fields/columns, issue templates, epics E00–E20 and their task sub-issues from `docs/epics/*.md` |

- Built-in **Project workflows** are enabled too: item closed → `Done`; PR merged → `Done`; item reopened → `In Progress`.
- **Dependabot** (`.github/dependabot.yml`): `nuget`, `npm`, `pub`, `github-actions`, `docker`, weekly, grouped by ecosystem.
  - It only proposes. `dependency-guard` still enforces the allowlist.
- `CODEOWNERS`: the owner is required for `docs/dependencies/**`, `.github/**` and `CLAUDE.md`.

## Brand, colors and the landing page

### Brand direction
- Young and energetic, but warm.
- The feeling must be **"we help each other"** (yardımlaşma) and **"you are our guest"** (qonaqpərvərlik).
- Inspiration: Azerbaijani hospitality, tea and the shared table (süfrə), the pomegranate (nar) as a symbol of abundance and sharing.
- The UI colleague may override the palette below. Change it only in `/design-tokens/tokens.json`.

| Token | Hex | Role |
|---|---|---|
| `nar` (primary) | `#E8475F` | Generosity, CTAs, gift moments |
| `saffron` (secondary) | `#FFB72B` | Warmth, highlights, badges |
| `mint` (accent) | `#16B89B` | Help/trust, success, social treating |
| `plum` (ink) | `#2A1A3E` | Text, dark sections |
| `cream` (surface) | `#FFF8EF` | Backgrounds |
| `blush` | `#FFE3E6` | Soft cards |
| Gradient `sunrise` | `#E8475F → #FFB72B` | Hero, gift cards |
| Gradient `together` | `#16B89B → #2A1A3E` | Social/impact sections |

- WCAG AA contrast is mandatory. Text on `saffron` must use `plum`.
- Fonts: **Manrope** (UI) and **Unbounded** (display headlines). Both are free and support Azerbaijani characters (ə, ğ, ı, ö, ü, ç, ş).
- Gift themes (Birthday, Love, Thanks, Family, Congrats, JustBecause, Corporate) each get their own illustration + accent color in the tokens.

### Landing page (`projects/landing`)
Sections, in order:
1. **Hero:**
   - headline "Qonaq et, xoşbəxt et"
   - an animated scene: a tea glass with steam (lottie-web) passing from hand to hand along a line, forming a "chain of kindness" that draws on scroll (CSS scroll-driven animation + SVG stroke)
   - CTA: app download + "Hədiyyə göndər"
2. **Live impact counters**: treats today, people treated, partner businesses. Count-up on view; data from `GET public/stats` (cached 60 s).
3. **How it works**: 3 steps with scroll-pinned animation (choose → send → enjoy at the café).
4. **Use cases**: interactive cards — dostuna kofe, sevdiyinə sürpriz, ailənə yemək, tələbəyə ısmarla, şirkət hədiyyələri. Each card has a hover/tap micro-animation.
5. **Social treating / yardımlaşma**: askıda story, active campaigns with animated progress bars, categories (from the API).
6. **Leaderboard teaser**: top 5 people/businesses this month, with a podium animation.
7. **For businesses**: benefits, QR redemption demo animation, "Partnyor ol" form.
8. **For companies**: corporate gifts and CSR, "Əlaqə" form.
9. **For developers**: integration API teaser → `/developers` page (overview, auth, webhooks, link to the OpenAPI docs).
10. **FAQ**, footer.

Also part of the landing app:
- the public gift page `/g/:token`
- the public leaderboard `/reytinq`
- campaign pages `/kampaniya/:slug`

Technical requirements:
- Lighthouse ≥ 90 on all four categories (mobile).
- Animations are lazy-loaded and run only on the client.
- No layout shift.
- Open Graph images per gift theme and campaign.

## Angular UI guidelines

> **PENDING — provided by the UI colleague.** When they arrive, they are written into this section verbatim (component library, layout grid, component rules, accessibility, motion rules, naming).
>
> Until then:
> - Scaffold apps, routing, auth, the API client, design tokens and page skeletons only.
> - Do **not** pick or install a component library.
> - Do **not** build final screens for the portals.
> - Epics `E17`, `E18` and `E19` stay at "skeleton" level until this section is filled in.

## Payments

### Payriff integration (must follow exactly)
- Base URL: `https://api.payriff.com`. App key from `Payriff:AppKey` in the `Authorization` header.
- Calls:
  - Create order: `POST /api/v3/orders` with `amount`, `currency=AZN`, `language=AZ`, `description`, `callbackUrl`, `operation=PURCHASE`. The response contains `orderId` and `paymentUrl`.
  - Get order: `GET /api/v3/orders/{orderId}`.
  - Refund: `POST /api/v3/refund` (full or partial amount).
- **The callback is unsigned.** Treat it as a trigger only: always confirm the status with Get Order.
- Fulfillment must be **idempotent**.
- A hosted job runs every 5 min. It re-checks `Pending` payments older than 2 min and expires those older than 30 min.
- Business payouts are **manual** bank transfers, recorded by the admin and tracked in the ledger.
- Check field names against the official Payriff docs. All Payriff DTOs live in `Infrastructure/Payments/Payriff`.

---

## Domain model

Storage placement follows "Architecture overview". Section **Collections** = MongoDB; section **PostgreSQL tables** = Postgres.

General rules:
- Identifiers are in English.
- Money is stored as `long` **qəpik**. Times are stored in UTC. Business days use **Asia/Baku**.
- Multi-document operations use transactions within one store. Cross-store effects go only through outbox events.
- Categories are referenced only by `categoryId`. Never branch on a category code in logic.
- Files are referenced by `fileId`, never by a raw object key.

### Soft delete (applies to every Mongo collection and Postgres table)
- **No hard deletes, ever.**
  - Forbidden in `src/`: `DeleteOne`, `DeleteMany`, `FindOneAndDelete`, Mongo TTL indexes, S3 `DeleteObject`, EF `Remove`/`RemoveRange`/`ExecuteDelete`, and raw SQL `DELETE`/`TRUNCATE`.
  - An architecture test fails the build if any of these appears.
  - Postgres:
    - EF `SaveChanges` interceptor converts any `Deleted` state to a soft delete. It throws for append-only tables.
    - A global query filter `!IsDeleted` is applied to every soft-deletable entity.
- Expiry is logical: compare `expiresAt` with now. This covers OTPs, tokens, gifts and claims.
- `BaseEntity` fields:
  - `id`
  - `createdAt`, `createdBy?`, `updatedAt`, `updatedBy?`
  - `isDeleted` (always present, default `false`), `deletedAt?`, `deletedBy?`, `deleteReason?`
- `SoftDeleteAsync(id, userId, reason)` and `RestoreAsync(id)` both write an audit log entry.
- The generic repository **adds `isDeleted: false` to every query**. Deleted records are visible only through `IncludeDeleted()`, which only admin endpoints use.
- Cascades are soft:
  - business → its catalog items and offers are soft-deleted, its active claims cancelled
  - category → can only be deactivated while in use (see Categories)
- **Append-only** records are never deleted at all. Corrections are reversing entries. These are `giftRedemptions` (Mongo) and the Postgres tables `ledger_entries`, `audit_logs`, `score_events`, `payments` (status changes only), `outbox_messages`, `inbox_messages` and `webhook_deliveries`.
- A user account delete = soft delete + login blocked. Partial unique indexes make the email reusable.

### Indexing rules
- **Unique indexes are partial** on `{ isDeleted: false }`:
  - `users.email`
  - `categories.code`
  - (`identityHashes.finHash`, `identityHashes.categoryId`)
  - `businesses.voen`
  - (`couponCodes.couponId`, `couponCodes.code`)
  - `gifts.linkTokenHash`
  - `businesses.slug`
- **Query indexes are partial** on `{ isDeleted: false }`, with equality fields first. Queries must include `isDeleted: false` literally; the repository guarantees this.
  - `offers`: (`businessId`, `type`), (`categoryId`, `type`)
  - `businesses`: `location` 2dsphere, (`status`, `acceptsPool`), (`status`, `acceptsNetworkVouchers`)
  - `catalogItems`: (`businessId`, `isActive`)
  - `claims`: (`recipientUserId`, `status`, `createdAt`), (`businessId`, `status`), (`status`, `expiresAt`)
  - `gifts`:
    - (`recipientUserId`, `status`), (`recipientEmail`, `status`), (`senderUserId`, `createdAt`)
    - (`businessId`, `status`), (`corporateOrderId`)
    - (`status`, `deliverAt`), (`status`, `expiresAt`)
  - `verificationRequests`: (`status`, `createdAt`), (`userId`, `categoryId`)
  - `giftRedemptions`: (`giftId`), (`businessId`, `createdAt`)
  - `otpCodes`: (`email`, `createdAt`)
  - `refreshTokens`: `tokenHash`
  - `files`: (`ownerType`, `ownerId`)
- Admin "include deleted" lists: one non-partial index (`isDeleted`, `deletedAt`) per soft-deletable collection.
- At startup, an idempotent migration:
  - backfills a missing `isDeleted` field
  - creates the indexes
  - drops conflicting old non-partial indexes
- **Postgres indexes:**
  - Unique and query indexes on soft-deletable tables are **partial**: `WHERE is_deleted = false`. Configure them in EF with `HasFilter("is_deleted = false")`.
  - Append-only tables use normal indexes.
  - Required:
    - `payments(provider_order_id)` unique; `payments(status, created_at)`
    - `ledger_entries(account_type, account_id, created_at)`; `ledger_entries(source_event_id)` unique (idempotency)
    - `score_events(subject_type, subject_id, source_type, source_id)` unique
    - `leaderboard_entries(subject_type, period_type, period_key, points DESC)`; `leaderboard_entries(subject_type, subject_id, period_type, period_key)` unique
    - `integration_clients(client_id)` unique partial
    - `webhook_deliveries(subscription_id, status, next_attempt_at)`
    - `outbox_messages(processed_at) WHERE processed_at IS NULL`
    - `inbox_messages(message_id, consumer)` unique

### Collections

- **users**
  - `email` (unique, lowercase), `firstName`, `lastName?`, `avatarFileId?`
  - `roles[]` (`User`, `BusinessStaff`, `BusinessOwner`, `CorporateUser`, `BrandUser`, `Admin`)
  - `businessId?`, `corporateId?`
  - `verifiedCategories[]`: `{categoryId, subtype?, validUntil?, verificationId}`
  - `isBlocked`, `fcmTokens[]`
  - Every user is eligible for every active category with `isOpen = true`.
- **otpCodes**
  - `email`, `codeHash`, `attempts`, `expiresAt`, `usedAt?`
  - 6 digits, valid 10 min, max 5 attempts, max 3 sends per 15 min.
- **refreshTokens**
  - `userId`, `tokenHash`, `expiresAt`, `revokedAt?`
- **files**
  - `bucket`, `objectKey` (immutable), `contentType`, `sizeBytes`, `sha256`
  - `ownerType`, `ownerId`, `purpose` (`VerificationDocument`, `Logo`, `Banner`, `CatalogImage`, `CouponCsv`, `RecipientCsv`, `GiftImage`)
  - A soft-deleted file is never served.
- **categories**
  - `code` (slug), `name`, `description`, `iconFileId?`, `color`, `sortOrder`, `isActive`
  - `isOpen`: no verification needed
  - `privacyLevel` (`Public` | `Sensitive`)
  - `verification`: `{ requiresFin, minFiles, maxFiles, documentHint, subtypes[]: {code, name}, fields[]: {key, label, type: Text|Select|Date, required, options[]}, validity: {type: FixedDate|Months|None, month?, day?, months?} }`
  - `limits`: `{ perDay, perMonth, poolAccess, poolSpendPerDayQepik }`
  - Creating a category auto-creates its default pool.
- **identityHashes**
  - `finHash` (HMAC-SHA256 of the upper-cased FIN with a server secret), `categoryId`, `userId`
  - Never store the raw FIN.
- **verificationRequests**
  - `userId`, `categoryId`, `subtype?`, `fields` (key → value), `finHash?`, `fileIds[]`
  - `status` (`Pending`, `Approved`, `Rejected`), `rejectReason?`, `reviewedBy?`, `reviewedAt?`
- **businesses**
  - `name`, `slug`, `type` (`Cafe`, `Restaurant`, `Bakery`, `Confectionery`, `Market`, `Pharmacy`, `Bookstore`, `Florist`, `Barber`, `Other`)
  - `voen`, `address`, `location`, `phone`, `email`, `logoFileId?`, `coverFileId?`, `iban`, `workingHours`
  - `acceptsPool`, `acceptsNetworkVouchers`, `voucherMinQepik`, `voucherMaxQepik`
  - `status` (`Pending`, `Active`, `Suspended`), `ownerUserId`
- **catalogItems**
  - `businessId`, `title`, `description?`, `priceQepik`, `imageFileId?`, `group?` (e.g. "İsti içkilər"), `isActive`, `isGiftable`
- **offers** (social treating)
  - `businessId`, `type` (`BusinessDaily` | `Suspended` | `Remote`), `title`, `categoryId`
  - `donorDisplay` (`Anonymous` | `Named`), `donorName?`
  - `BusinessDaily`: `dailyQuota`, `usedToday`, `quotaDate`, `availableFrom`/`availableTo`, `isActive`
  - `Suspended`: `totalQuantity`, `remaining`, `donorUserId?`, `createdByStaffId`
  - `Remote`: `catalogItemId`, `unitPriceQepik`, `totalQuantity`, `remaining`, `donorUserId`, `paymentId`
- **pools**
  - `categoryId`, `campaignId?`, `balanceQepik`, `reservedQepik`
- **poolDonations**
  - `poolId`, `donorUserId?`, `corporateId?`, `amountQepik`, `donorDisplay`, `paymentId?`
  - Used to show "named" donors on pool-funded claims, with proportional attribution: a pool claim shows the name of the oldest named donation that still has unused value.
- **campaigns**
  - `title`, `description`, `bannerFileId?`, `categoryId`, `startsAt`, `endsAt`, `goalQepik`, `raisedQepik`, `corporateId?`
  - `status` (`Draft`, `Active`, `Ended`)
  - When a campaign ends, its unused balance rolls over to the category's default pool, with a ledger entry.
- **claims** (social treating)
  - `recipientUserId`, `businessId`, `source` (`Offer` | `Pool`), `offerId?`, `poolId?`, `catalogItemId?`, `amountQepik?`
  - `donorDisplayText?`: shown to the recipient, e.g. "Leyla sizə ısmarladı", or "Bir xeyirxah insan sizə ısmarladı"
  - `status` (`Reserved`, `Redeemed`, `Expired`, `Cancelled`)
  - `qrToken`, `shortCode`, `expiresAt`, `redeemedAt?`, `redeemedByStaffId?`
- **gifts** (gifting pillar)
  - What is gifted:
    - `kind` (`Items` | `Voucher`)
    - `scope` (`Business` | `Network`); `Network` requires `kind = Voucher`
    - `businessId?` (required when the scope is `Business`)
    - `items[]`: `{catalogItemId, title, unitPriceQepik, qty, redeemedQty}` (when `Items`)
    - `amountQepik`, `remainingQepik` (when `Voucher`; for `Items`, the total)
    - `fulfillmentType` (`Pickup`)
  - Who sends and receives:
    - `senderUserId?`, `corporateOrderId?`, `senderDisplay` (`Named` | `Anonymous`), `senderName` (snapshot; the corporate name for corporate gifts)
    - `recipientName`, `recipientEmail?`, `recipientUserId?` (set when the email matches an account or when the recipient claims the link)
  - Presentation and timing:
    - `theme` (`Birthday`, `Love`, `Thanks`, `Family`, `Congrats`, `JustBecause`, `Corporate`), `message?` (≤500 chars)
    - `deliverAt` (now or scheduled, ≤90 days ahead), `expiresAt` (`deliverAt` + `Gifts:ValidityDays`, default 60)
  - Link and status:
    - `linkTokenHash` (the raw token is 32 random bytes, base64url, never stored)
    - `status` (`AwaitingPayment`, `Scheduled`, `Delivered`, `Opened`, `PartiallyRedeemed`, `Redeemed`, `Expired`, `Refunded`, `Cancelled`)
    - `paymentId?`, `deliveredAt?`, `openedAt?`
  - `expiryAction` (`RefundToPayer` | `DonateToPool`), `expiryPoolId?`
- **giftRedemptions** (append-only)
  - `giftId`, `businessId`, `staffUserId`
  - `lines[]` (for items: `{catalogItemId, qty}`) or `amountQepik` (for vouchers)
  - `valueQepik`, `createdAt`
- **corporates**
  - `name`, `voen`, `logoFileId?`, `brandColor?`, `contactEmail`
- **corporateGiftOrders**
  - `corporateId`, `createdByUserId`
  - `template`: gift fields without the recipient
  - `recipientsFileId`, `recipientCount`, `totalQepik`
  - `paymentMethod` (`Card` | `BankTransfer`), `paymentId?`, `bankReference?`
  - `status` (`Draft`, `AwaitingPayment`, `Paid`, `Sending`, `Completed`, `Cancelled`)
  - `expiryAction`, `expiryPoolId?`
- **brands**, **coupons**, **couponCodes**: QONAQET Plus
  - Coupon: `eligibleCategoryIds[]` (empty = everyone), `codeType` (`Shared` | `Unique`), `perUserLimit`, validity.
- **reports**
  - `reporterUserId`, `targetType` (`Business` | `User`), `targetId`, `refType?`, `refId?`, `reason`, `status`
- **outboxMessages**: `type`, `payload`, `occurredAt`, `processedAt?`, `attempts` (relay to RabbitMQ).
- users additionally have: `leaderboardOptIn` (default `true`), `leaderboardName?` (nickname), `level`, `badges[]`.

### PostgreSQL tables (EF Core, snake_case, `qonaqet` schema)
Shared columns on soft-deletable tables: `id uuid`, `created_at`, `created_by`, `updated_at`, `updated_by`, `is_deleted`, `deleted_at`, `deleted_by`, `delete_reason`. Money is `bigint` qəpik. All timestamps are `timestamptz`.

- **payments** (status changes only)
  - `payer_user_id?`, `corporate_id?`, `partner_client_id?`
  - `purpose` (`Gift` | `CorporateGiftOrder` | `Remote` | `Pool` | `Campaign` | `PartnerTopUp`)
  - `amount`, `provider`, `provider_order_id`
  - `status` (`Pending`, `Paid`, `Failed`, `Expired`, `Refunded`, `PartiallyRefunded`), `refunded_amount`
  - `payload jsonb`, `paid_at?`
- **refunds**: `payment_id`, `amount`, `reason`, `provider_ref`, `status`, `requested_by`
- **ledger_entries** (append-only, double-entry style per account)
  - `account_type` (`Business` | `Pool` | `Corporate` | `PartnerClient` | `Platform`), `account_id`
  - `type` (`PoolDeposit`, `PoolReserve`, `PoolRelease`, `RedemptionCredit`, `Commission`, `Payout`, `CampaignRollover`, `GiftExpiryDonation`, `Refund`, `PartnerTopUp`, `PartnerIssue`)
  - `amount` (signed), `ref_type`, `ref_id`, `source_event_id` (unique → idempotent consumer), `created_at`
  - A balance is `SUM(amount)` per account.
  - The pool balance in Mongo is the operational value. A nightly reconciliation job compares it with the ledger and alerts on mismatch.
- **payouts**: `business_id`, `amount`, `bank_reference`, `created_by_admin_id`
- **score_events** (append-only): `subject_type` (`User` | `Business` | `Corporate`), `subject_id`, `source_type`, `source_id`, `points`, `treats`, `value`, `created_at`
- **leaderboard_entries**: `subject_type`, `subject_id`, `period_type` (`Week` | `Month` | `AllTime`), `period_key`, `points`, `treats`, `value`, `display_name`, `avatar_file_id?`. Updated with `INSERT ... ON CONFLICT DO UPDATE`.
- **audit_logs** (append-only): `actor_id`, `actor_type`, `action`, `entity_type`, `entity_id`, `data jsonb`, `ip`, `created_at`
- **integration_clients**
  - `name`, `client_id`, `secret_hash`
  - `owner_type` (`Business` | `Corporate` | `Partner`), `owner_id`, `business_ids uuid[]`
  - `scopes text[]`, `is_sandbox`, `rate_limit_per_minute`, `last_used_at`
- **webhook_subscriptions**: `client_id`, `url`, `secret_hash`, `event_types text[]`, `is_active`
- **webhook_deliveries** (append-only status log): `subscription_id`, `event_id`, `event_type`, `payload jsonb`, `status`, `attempt`, `next_attempt_at`, `response_code`, `response_body_snippet`
- **idempotency_records**: `client_id`, `key`, `request_hash`, `response jsonb`, `created_at`. Kept as history; the Valkey copy is only a cache.
- **outbox_messages** / **inbox_messages**: as described in "Architecture overview".

---

## Core business rules

### A. Gifting

**Creating a personal gift** (`POST gifts` → payment):
1. Choose what to gift:
   - `Items`: 1–10 giftable catalog items of one business (`isGiftable`).
   - `Voucher` at a business: an amount between the business's `voucherMinQepik` and `voucherMaxQepik`.
   - `Network` voucher: 2–200 AZN, usable at any business with `acceptsNetworkVouchers = true`.
2. Choose the recipient, by either:
   - an email, or
   - "Linki özüm göndərəcəm": no email; the sender gets the share link once delivered.
   - A recipient name is required.
3. Choose the theme, message, `senderDisplay` and `deliverAt`.
4. The server computes the price, then creates the gift (`AwaitingPayment`) and the payment.
5. On a confirmed payment: set the gift to `Scheduled`, generate the link token and return the share link to the sender once.

**Delivery** (hosted job, every minute; picks gifts in `Scheduled` with `deliverAt ≤ now`):
- Set the status to `Delivered`.
- If `recipientEmail` is set: send a themed email with the link `{PublicBaseUrl}/g/{token}`, the sender (or "Anonim dost"), the message and the gift contents.
- If the email belongs to a user: also send a push, and set `recipientUserId`.
- Notify the sender: "Hədiyyəniz çatdırıldı".

**Opening and claiming:**
- `/g/{token}` is a public page in the `landing` app (SSR, no login). It shows:
  - the themed card, sender, message and contents
  - remaining items/balance
  - the redemption QR code and short code
  - the business(es) on a map
  - the expiry date
- The first open sets `Opened` and notifies the sender ("Hədiyyənizə baxıldı 🎉").
- "Hesabıma əlavə et" (login with email OTP) attaches the gift to the account (`recipientUserId`). After that the gift also shows in the app under "Hədiyyələrim". The link still works for the same gift, so parents without the app can still use it.
- **The link is a bearer credential.** Rate-limit the endpoint and never log tokens. The sender can **re-issue** the link: a new token is generated and the old hash is replaced.

**Redemption at the business:**
- Gift QR payload: `base64url("g." + giftId + "." + HMACSHA256(giftId, secret))`. It is reusable until fully redeemed; the short code is stable for the gift.
- Staff scan the code. They see only the gift contents and what remains, never the recipient's or sender's data. They select what to redeem:
  - `Items`: per-line quantities ≤ remaining
  - `Voucher`: any amount ≤ remaining (**partial redemption allowed**)
  - `Network`: same as voucher, at any business with `acceptsNetworkVouchers`
- One **Mongo** transaction:
  - create a `giftRedemptions` entry
  - update the remaining quantities/amount and the status (`PartiallyRedeemed` / `Redeemed`)
  - write a `GiftRedeemed` outbox event
- The consumer (Postgres, idempotent on `source_event_id`) writes:
  - ledger: `RedemptionCredit` = value, `Commission` = −7% for the redeeming business
  - score events
  - webhooks
- Concurrency: the update is conditional on the current remaining value (optimistic `version` field). Retry once, then return `gift_changed`.
- Both sender and recipient get a push/email: "Hədiyyə istifadə edildi". An anonymous sender is still notified.

**Expiry** (hosted job):
- 7 days before `expiresAt`: remind the recipient.
- At expiry, the unredeemed value follows `expiryAction`:
  - `RefundToPayer`: partial Payriff refund of the remaining value (the default for personal card gifts)
  - `DonateToPool`: move the value to `expiryPoolId` (or the `everyone` default pool) as a `GiftExpiryDonation` ledger entry. This is the default for corporate bank-transfer orders. The personal sender may choose it at checkout ("İstifadə olunmasa, ehtiyacı olanlara bağışla").
- Status becomes `Expired` (+ `Refunded` when refunded).

**Cancellation:** the sender can cancel a gift that is still `Scheduled`. The money is fully refunded.

**Corporate gifts and bonuses:**
1. A corporate user creates an order in the web portal:
   - template (kind/scope/business/items or amount, theme `Corporate` + message, branding from the corporate profile)
   - recipients CSV (`name,email`; max 5,000 rows; validated with a row-level error report)
   - `deliverAt`, `expiryAction`
2. Total = per-gift price × recipients.
3. Payment:
   - by card (Payriff), or
   - by bank transfer: the order waits in `AwaitingPayment` until an admin confirms it with a bank reference.
4. After payment, a background job creates one gift per recipient (`corporateOrderId` set, `senderName` = corporate name), in batches of 200, idempotently, with status `Sending` → `Completed`.
5. The corporate portal shows delivered/opened/redeemed counts, the unused value and a CSV/PDF report. It never shows what individual people bought beyond redeemed yes/no per recipient email.

### B. Social treating (categories)

Seeded categories (initial data; editable; the admin can add more):

| code | isOpen | privacy | proof | subtypes / fields | validity | FIN | perDay | perMonth | pool | poolSpend/day |
|---|---|---|---|---|---|---|---|---|---|---|
| `everyone` | yes | Public | — | — | None | no | 1 | 15 | no | 0 |
| `student` | no | Public | student certificate (e-gov.az arayış or student card) | field `university` (Select) | FixedDate 30 Sep | yes | 2 | 40 | yes | 5 AZN |
| `veteran-family` | no | Sensitive | e-sosial.az certificate | `qazi`, `martyr-family` | Months 12 | yes | 3 | 60 | yes | 15 AZN |
| `in-need` | no | Sensitive | ÜSY certificate or municipality/NGO letter | — | Months 6 | yes | 3 | 60 | yes | 15 AZN |

Category rules:
- Verification for a non-open category requires:
  - a valid subtype (if the category defines subtypes) and the required fields
  - a FIN (if `requiresFin`)
  - between `minFiles` and `maxFiles` files (PDF/JPG/PNG, ≤5 MB each)
- An admin approves or rejects every request, and can override `validUntil`.
- A FIN hash is unique per category (`identity_already_used`).
- 14 days before `validUntil`: push + email "Statusunuzu yeniləyin".
- A category's code cannot change once the category is in use. A category with active offers, campaigns, a pool balance or valid verifications cannot be soft-deleted; it can only be deactivated.
- All category-dependent UI is rendered from `GET categories`. There are no category-specific screens.

Limits:
- Max 1 active `Reserved` claim per user, and 1 claim per offer per day.
- The most generous limits among the user's categories apply.
- Limits apply to social treating only. **Gifts addressed to a person have no category limits.**

Claiming:
- **Offer claim:**
  - Decrement atomically with one conditional `FindOneAndUpdate` (`remaining > 0` / `usedToday < dailyQuota`).
  - Reset the daily quota lazily (Baku date).
- **Pool claim:**
  - A catalog item at an `acceptsPool` business.
  - Pools: only those of the user's eligible categories with `poolAccess`; active campaign pools first (soonest ending), then the default pool.
  - In a Mongo transaction: check `balance − reserved ≥ price`, reserve, and write an outbox event (→ `PoolReserve` ledger entry).
- **Reservation TTL** is 30 min. A hosted job expires stale claims and releases the unit or reservation.
- The claim carries a `donorDisplayText`, which the recipient sees:
  - for a named donor: "Leyla sizə ısmarladı 💚"
  - otherwise: "Bir xeyirxah insan sizə ısmarladı 💚"

Redemption of claims:
- One-time QR: `base64url("c." + claimId + "." + HMAC)`, or the short code. It is idempotent (`already_redeemed`).
- Ledger (via the `ClaimRedeemed` event):
  - no money for `BusinessDaily` / `Suspended`
  - `Remote` / `Pool` → credit + 7% commission (pool: decrease balance and reserved in Mongo, in the redemption transaction)

Privacy:
- Business endpoints show only the item, status and amount.
- The category name is shown only if `privacyLevel = Public`; otherwise the label is "Qonaq".
- Donors see only aggregates.

### C. Business panel
- **Onboarding:**
  - A user applies (mobile or web) with: name, type, VÖEN, address, map pin, phone, email, IBAN, working hours.
  - An admin activates the business, and the applicant becomes `BusinessOwner`.
- **Owner:**
  - staff (invite by email; remove = soft delete of the role link)
  - catalog (groups, prices, images, `isGiftable`)
  - offers
  - settings: `acceptsPool`, `acceptsNetworkVouchers`, voucher min/max
- **Staff:** redeem (scan or code), and view today's redemptions.
- **Reports:**
  - gifts sold (count/value), redemptions by source (gift / network voucher / pool / remote / daily / suspended)
  - commission, balance, payouts
  - date filters, CSV export
- **Network vouchers:** the redeeming business is credited, regardless of where the voucher was bought.

### D. Payments (shared)
- `POST payments` is never called directly by clients. It is created inside the purpose endpoints (`gifts`, `corporate/orders/{id}/pay`, `donations/remote`, `donations/pool`, `donations/campaign`). Amounts are always computed on the server.
- The app opens `paymentUrl`, then polls `GET payments/{id}`.
- On a confirmed `Paid`:
  - A Postgres transaction marks the payment `Paid` and writes a `PaymentConfirmed` outbox event.
  - A consumer fulfills per purpose in Mongo (gift → `Scheduled`, pool credit, offer creation), idempotently by status transition.
- Refunds (admin, or automatic for gift expiry/cancel):
  - partial refunds are supported
  - they are tracked in `refundedQepik`, with a `Refund` ledger entry
- Pool and campaign donations are non-refundable, which the UI states before payment.

### E. Leaderboard (Reytinq)
**Only real treats count.** Points are awarded at **redemption**, never at purchase, so a gift nobody used gives no points.

Who earns points, and how many (values in `Leaderboard:Points`, configurable):

| Earner | What counts | Points |
|---|---|---|
| User (sender) | personal gift: per redeemed item unit, or per started 5 AZN of redeemed voucher value | 1 |
| User (donor) | social treat: remote / named or anonymous pool donation share / campaign | 2 per redeemed claim |
| Business | `BusinessDaily` and `Suspended` claims redeemed at that business | 2 per claim |
| Corporate | each redeemed corporate gift and each redeemed claim funded by its campaigns | 1 per gift, 2 per claim |

For pool claims, the points go to the donor of the funding donation (oldest unused donation first, the same attribution as `donorDisplayText`).

Anti-gaming rules:
- No points when the gift's sender and recipient are the same account, or when the recipient never verified an email (link-only gifts count only after "Hesabıma əlavə et").
- Max 10 points per sender→recipient pair per month.
- Max 300 points per user per day; anything above is recorded with `points = 0`.
- Blocked users and suspended businesses are hidden from the boards.
- Refunded or reversed redemptions write negative score events.

Display:
- Boards: **İnsanlar · Bizneslər · Şirkətlər**, each weekly / monthly / all-time. Top 100 are shown, plus "Sizin yeriniz: #N".
- Weekly and monthly winners get a badge (`Week Top 3`, `Month Top 10`). A hosted job awards them at period end (Baku time), idempotently.
- Levels by all-time points:
  - Qonaq (0)
  - Qonaqpərvər (50)
  - Süfrə sahibi (200)
  - El ağsaqqalı (1000)
  - Səxavət ustası (5000)
- **Privacy:**
  - A user appears only if `leaderboardOptIn`, under `leaderboardName` or first name + initial.
  - Anonymous treats still earn points, but the board never reveals what was treated or for whom.
  - Sensitive categories are never mentioned on boards.
  - Users can opt out at any time.
- **Badge on the business page:** "Bu ayın ən qonaqpərvər biznesi" when the business is #1 in the monthly board.

### F. Other
- **Campaigns:** 4 `Draft` templates are seeded (Ramazan, Novruz, 27 Sentyabr — Anım Günü, 8 Noyabr — Zəfər Günü). Active campaigns appear on the home screen with a goal progress bar.
- **CSR:**
  - Corporate campaigns are funded by card or by a bank transfer that the admin records.
  - Reports are PDF (HTML → Playwright) and CSV, with no personal data.
- **QONAQET Plus:** coupons, either for everyone or limited to categories; unique codes are assigned atomically.
- **Blocking and reports:**
  - Users and businesses can report each other.
  - Blocked users cannot claim or create gifts; their active claims are cancelled.
  - A suspended business's gifts stay valid only if they are network vouchers. Its business-scoped gifts are refunded or donated according to `expiryAction`, after admin confirmation.

---

## API conventions

- Prefix: `/api/v1`.
- Errors: ProblemDetails with stable `code`s, e.g.:
  - `quota_exhausted`, `category_not_eligible`, `claim_expired`, `pool_insufficient`, `limit_reached`
  - `identity_already_used`, `payment_not_confirmed`
  - `gift_expired`, `gift_insufficient_balance`, `gift_changed`, `already_redeemed`
- Validation: own validators. OpenAPI document (built-in) in Development.
- Rate limits on OTP, claim, gift-link, redeem and payment endpoints.
- Paging: `?page=&pageSize=` (max 50) → `{ items, total }`.
- `DELETE` always means soft delete. Admin lists accept `?includeDeleted=true`, and every entity has `POST .../{id}/restore`.

Main endpoints:
- **Auth:** `POST auth/otp/send`, `POST auth/otp/verify`, `POST auth/refresh`, `POST auth/logout`
- **Me:** `GET/PATCH/DELETE me`, `POST me/fcm-token`, `GET me/claims`, `GET me/donations`
- **Catalog and discovery:**
  - `GET categories`
  - `GET businesses?lat=&lng=&radiusKm=&type=&acceptsPool=&acceptsNetworkVouchers=`
  - `GET businesses/{idOrSlug}` (with catalog)
  - `GET offers?...&categoryId=`
  - `GET campaigns/active`
  - `GET pools/summary`
- **Gifts:**
  - `POST gifts` → `{giftId, paymentUrl}`
  - `GET gifts/sent`, `GET gifts/received`, `GET gifts/{id}`
  - `POST gifts/{id}/cancel`, `POST gifts/{id}/reissue-link`
  - Public: `GET public/gifts/{token}` and `POST public/gifts/{token}/claim` (needs auth)
- **Social:**
  - `POST offers/{id}/claim`, `POST pool-claims`, `POST claims/{id}/cancel`
  - `POST donations/remote`, `POST donations/pool`, `POST donations/campaign`
- **Payments:** `GET payments/{id}`, `POST payments/payriff/callback` (anonymous), `GET payments/fake/{id}` (Development)
- **Verification:** `POST verification` (multipart), `GET verification/me`
- **Plus:** `GET coupons`, `POST coupons/{id}/reveal`
- **Leaderboard:** `GET leaderboard?subjectType=&periodType=&periodKey=` (top 100 + `me`), `GET me/score` (level, badges, history), `PATCH me/leaderboard` (opt-in, nickname)
- **Business:**
  - `POST businesses/apply`
  - `business/catalog`, `business/offers`, `POST business/offers/suspended`
  - `POST business/redeem/preview` (scan → contents) and `POST business/redeem` (selection)
  - `GET business/redemptions`, `GET business/sales`, `GET business/ledger`, `GET business/export.csv`
  - `business/staff`, `PATCH business/settings`
- **Corporate:**
  - `corporate/orders` (create, upload CSV, validate, pay, list, report)
  - `corporate/campaigns`, `.../report.pdf|.csv`
- **Admin:**
  - categories, verifications, businesses, users, gifts (search by id or email, re-send email, refund)
  - corporate orders (confirm bank transfer), corporates, campaigns, pools (manual deposit)
  - payouts, payments/refunds, brands/coupons, reports, stats, audit log
- **Reports:** `POST reports`
- **Public site (anonymous, cached):** `GET public/stats`, `GET public/leaderboard/top`, `GET public/campaigns/{slug}`, `POST public/partner-applications`, `POST public/corporate-inquiries`
- **Partner API:** separate host and prefix `/partner/v1` (see "Integration platform").

## Backend structure (Clean Architecture + CQRS)

Dependency rule: `Domain` ← `Application` ← `Infrastructure` ← hosts (`Api`, `Integration.Api`, `Worker`). Own architecture tests enforce it.

```
backend/
  Qonaqet.sln
  Directory.Build.props            nullable, warnings-as-errors, analyzers, LangVersion
  Directory.Packages.props         central package versions
  src/
    BuildingBlocks/
      Qonaqet.SharedKernel/        Result<T>, Error, Money (long qəpik), Clock (Baku), Guard, Entity/AggregateRoot,
                                   ISoftDeletable, DomainEvent, StronglyTypedId<T>
    Qonaqet.Domain/                pure domain, no external packages
      Users/          User, Role, VerifiedCategory
      Categories/     Category, VerificationConfig, Limits, Validity (+ ValidityCalculator)
      Verification/   VerificationRequest, IdentityHash
      Businesses/     Business, StaffMembership, WorkingHours
      Catalog/        CatalogItem
      Social/         Offer (BusinessDaily/Suspended/Remote), Claim, Pool, PoolDonation, Campaign
      Gifts/          Gift, GiftItem, GiftRedemption, GiftTheme, GiftLink
      Corporate/      Corporate, CorporateGiftOrder
      Payments/       Payment, Refund
      Ledger/         LedgerEntry, LedgerAccount, CommissionPolicy
      Leaderboard/    ScoreEvent, ScoringPolicy, Level, Badge
      Plus/           Brand, Coupon, CouponCode
      Integration/    IntegrationClient, WebhookSubscription, WebhookDelivery
      Moderation/     Report
      Files/          StoredFile
    Qonaqet.Application/
      Abstractions/
        Messaging/    ICommand<T>, IQuery<T>, ICommandHandler, IQueryHandler, IEventBus, IOutbox
        Persistence/  repository interfaces per aggregate, IMongoUnitOfWork, IPostgresUnitOfWork
        Services/     IPaymentGateway, IEmailSender, IPushSender, IFileStorage, ICache, IDistributedLock,
                      ILeaderboardStore, ICurrentUser, IClock, IPdfRenderer, ICsvService
      Behaviors/      Logging → Validation → Authorization → Idempotency → UnitOfWork (transaction + outbox flush)
      Features/       one folder per area; one folder per use case (vertical slice):
        Gifts/
          Commands/CreateGift/          CreateGiftCommand.cs, CreateGiftHandler.cs, CreateGiftValidator.cs
          Commands/RedeemGift/          ...
          Commands/CancelGift/, ReissueGiftLink/, ClaimGiftToAccount/
          Queries/GetReceivedGifts/     GetReceivedGiftsQuery.cs, Handler, GiftListItemDto.cs
          Queries/GetPublicGift/
          Events/                       GiftRedeemedHandler (ledger, score, webhooks, notifications)
          Jobs/                         DeliverScheduledGiftsJob, ExpireGiftsJob
        Auth/, Users/, Categories/, Verification/, Businesses/, Catalog/, Social/, Payments/,
        Ledger/, Corporate/, Leaderboard/, Plus/, Integration/, Moderation/, Files/, Admin/, PublicSite/
      Common/         Pagination, mapping extensions, error codes (Errors.Gift.Expired ...)
    Qonaqet.Contracts/            integration events (GiftRedeemed, ClaimRedeemed, PaymentConfirmed ...),
                                  versioned public webhook payload DTOs; no dependencies
    Qonaqet.Infrastructure/
      Persistence/
        Mongo/        MongoContext, class maps, SoftDeleteRepository<T>, repositories, IndexRunner, Migrations/, Seed/
        Postgres/     QonaqetDbContext, Configurations/, Interceptors/ (SoftDelete, Audit, Outbox), Migrations/
      Messaging/      Outbox (Mongo + Postgres), OutboxRelay, RabbitMqEventBus, Inbox, consumer host
      Caching/        Valkey cache, locks, rate limiter, LeaderboardStore
      Payments/Payriff/, Payments/Fake/
      Email/          MailKitEmailSender, Templates/ (own {{placeholder}} engine, themed gift templates)
      Storage/        S3FileStorage (AWSSDK.S3 → RustFS, no delete)
      Push/           FcmPushSender
      Documents/      PlaywrightPdfRenderer, own CSV, own QR encoder
      Jobs/           own scheduler (BackgroundService, advisory locks, job_runs)
      Identity/       JWT issuing, refresh tokens, OTP, client-credentials tokens
      DependencyInjection.cs
    Qonaqet.Api/                  controllers by area (thin: map request → command/query → Result → HTTP),
                                  middleware (correlation id, exception → ProblemDetails), auth policies, Program.cs
    Qonaqet.Integration.Api/      partner controllers, OAuth2 token endpoint, scope policies, Idempotency-Key filter,
                                  per-client rate limits, separate OpenAPI doc ("Partner API v1")
    Qonaqet.Worker/               job scheduler, outbox relay, RabbitMQ consumers, webhook dispatcher
  tests/
    Qonaqet.Domain.Tests/
    Qonaqet.Application.Tests/    handlers with fakes
    Qonaqet.IntegrationTests/     WebApplicationFactory + Testcontainers (all stores)
    Qonaqet.ArchitectureTests/    layer rules, no hard deletes, no category-code branching, naming conventions
```

### Conventions
- **Commands** change state and return `Result<TId>` or `Result`. **Queries** never change state. They read with projections straight from Mongo/Postgres (no repository needed) and return DTOs.
- **One handler per use case.** Controllers contain no logic.
- Domain events are raised inside aggregates. The `UnitOfWork` behavior stores them in the outbox in the same transaction.
- Integration events (`Qonaqet.Contracts`) are versioned (`GiftRedeemed.v1`). They are never changed in place.
- Authorization lives in the pipeline (policies plus a resource check in the handler, e.g. "staff belongs to this business").
- `Idempotency-Key` is required on money-moving POSTs (app and partner APIs). The stored response is replayed for the same key and body; the same key with a different body returns 409.
- Error codes are stable strings, defined in `Application/Common/Errors`.

### Background work (Worker, own scheduler)
Recurring jobs:
- payment re-check (5 min)
- claim expiry (1 min)
- gift delivery (1 min)
- gift expiry + reminders (15 min)
- verification expiry reminders (daily)
- corporate order fan-out (on demand)
- campaign end/rollover (5 min)
- leaderboard period-end badges (hourly check)
- pool/ledger reconciliation (nightly)
- webhook retries (1 min)

Each job claims its work through an atomic status transition, so a second instance cannot double-process.

### Seed data (Development)
- an admin
- the 4 categories and their pools (universities as `student.university` options)
- the 4 campaign templates
- 6 demo businesses (various types, catalogs; 3 accept network vouchers)
- 1 corporate + user
- 1 brand + 2 coupons
- 1 sandbox integration client with a webhook to a local echo endpoint

---

## Integration platform (Partner API)

Goal: any restaurant, café chain, POS system, delivery app, bank or other service can integrate in a day.

### Who integrates, and how
| Partner type | Typical use | Scopes |
|---|---|---|
| **POS / restaurant system** | Verify and redeem QONAQET codes at the till; sync the menu | `codes:read`, `redemptions:write`, `catalog:write`, `webhooks` |
| **Chain / franchise** | One client for many businesses (`business_ids[]`) | same as above |
| **Issuer** (bank, delivery app, HR platform, loyalty program) | Issue QONAQET gifts/vouchers to their users, paid from a prepaid partner balance | `gifts:issue`, `gifts:read`, `webhooks` |
| **Brand (Plus)** | Validate and mark coupon codes as used | `coupons:validate` |

### Authentication and safety
- OAuth2 **client credentials**: `POST /oauth/token` (`client_id`, `client_secret`, `scope`) returns a JWT valid for 1 hour.
  - Secrets are shown once and stored hashed.
  - Secrets can be rotated, with both secrets valid during a 24 h overlap.
- Every client has `is_sandbox`:
  - sandbox clients work only against sandbox businesses and the Fake payment gateway
  - test codes are documented
- Per-client rate limits (Valkey). An `Idempotency-Key` header is **required** on every POST.
- Every call is logged in `audit_logs` with the client id. Partner responses never contain personal data, beyond what the partner itself sent.

### Endpoints (`/partner/v1`)
- `GET codes/{code}`: preview a gift, claim or network voucher (type, remaining items/amount, expiry, valid at this business?)
- `POST redemptions`: `{code, businessId, lines[] | amount, externalReference}`. The same rules apply as for staff redemption, including partial redemption.
- `POST redemptions/{id}/reverse`: allowed within 15 min. Writes reversing ledger and score entries and restores the remaining balance.
- `GET redemptions?businessId=&from=&to=`
- `PUT catalog/{businessId}`: bulk upsert by `externalId`. Items that are missing are soft-deleted only if `prune=true`.
- `GET catalog/{businessId}`
- `POST gifts` (issuers): same model as personal gifts, plus `externalReference`. Debited from the partner's prepaid balance (`PartnerIssue` ledger entry). Fails with `insufficient_partner_balance`.
- `GET gifts/{id}`, `POST gifts/{id}/cancel` (refund to the partner balance)
- `GET balance`, `GET ledger`
- `POST coupons/validate`: `{couponId, code}` → marks the code used
- `GET/POST/DELETE webhooks` (DELETE = soft delete)

### Webhooks (outbound)
- Events:
  - `gift.created`, `gift.delivered`, `gift.redeemed`, `gift.expired`
  - `redemption.created`, `redemption.reversed`
  - `payout.created`, `catalog.synced`
  - `partner.balance.low`
- Headers:
  - `X-Qonaqet-Event`, `X-Qonaqet-Delivery`
  - `X-Qonaqet-Timestamp`
  - `X-Qonaqet-Signature: sha256=HMAC(secret, timestamp + "." + body)`
- Receivers must reject requests older than 5 minutes.
- Retries with exponential backoff: 1 m, 5 m, 30 m, 2 h, 12 h, 24 h. After that the delivery is marked `Failed`. Partners can redeliver manually from the portal.
- Payloads are versioned DTOs from `Qonaqet.Contracts`.

### Developer experience
- A separate OpenAPI document, "QONAQET Partner API v1", with examples.
- `/developers` pages on the landing site.
- `docs/integration-guide.md`: quick start, auth, codes, webhook signature verification samples in C#, JS and PHP.
- The OpenAPI JSON is published as a CI artifact and can be imported directly into Postman or Insomnia (no generator dependency).
- The **business portal** has an "İnteqrasiyalar" section:
  - create, rotate and revoke clients (revoke = soft delete)
  - webhook subscriptions
  - delivery log with redeliver
  - sandbox toggle
  - request log
- Issuer and brand partners use the same portal under their own role.
- Partner balances are topped up by bank transfer (the admin confirms it) or by card (`PartnerTopUp` payment).

---

## Mobile app (Flutter)

Folder structure:
```
mobile/lib/
  main_dev.dart, main_staging.dart, main_prod.dart
  app/            app.dart, router.dart, theme/ (generated from design tokens), l10n/
  core/           network (http + own ApiClient), storage, errors, utils, widgets (shared)
  features/
    auth/         data/ (datasources, repositories impl, dtos) · domain/ (entities, repositories, usecases) · presentation/ (pages, widgets, providers)
    home/ discover/ business/ gifts/ social/ verification/ leaderboard/ plus/ profile/ business_mode/
```

- One app, Azerbaijani UI.
- Bottom tabs: **Ana səhifə · Kəşf et · Hədiyyə et (+) · Hədiyyələrim · Profil**.
- A mode switcher to **"Biznes"** appears for business roles.
- Deep links: `{PublicBaseUrl}/g/:token` opens the gift inside the app when it is installed.

User mode screens:
- **Auth:** email → 6-digit code → first name.
- **Ana səhifə:**
  - quick actions ("Dostuna kofe", "Ailənə yemək", "Sevdiyinə sürpriz", "Tələbəyə ısmarla")
  - campaign carousel, nearby businesses, active social offers, top-3 leaderboard widget
- **Kəşf et:** list + map, filters (business type, category offers, accepts network vouchers / pool).
- **Business page:** catalog, "Hədiyyə et", "İsmarla" (social), "Hovuzdan al" (if eligible).
- **Hədiyyə et flow:**
  1. what
  2. to whom (email or "linki özüm göndərəcəm" + name)
  3. theme + message + named/anonymous
  4. when
  5. expiry choice
  6. summary → payment → success with a share sheet
- **Hədiyyələrim:**
  - Received: card, QR + code, remaining, map, expiry.
  - Sent: status timeline; cancel / re-issue link.
- **Social:** "Ismarla" flow (named/anonymous), "Aldıqlarım" claims with QR, donor totals.
- **Status:** dynamic verification wizard from the category config.
- **Reytinq:** İnsanlar / Bizneslər / Şirkətlər, period switch, my place, level progress, badges, a points push after redemption.
- **QONAQET Plus**, **Profil** (leaderboard opt-in and nickname), **Biznes müraciəti**, **Şikayət et**.

Business mode:
- scan → preview → select items/amount → confirm
- manual code entry
- today's redemptions
- askıda entry
- quick stats

Design:
- Colors and fonts come **only** from the generated design tokens (see "Brand, colors and the landing page").
- Tone: warm and playful for gifts, respectful for social treating. Never use "kasıb"-type words.
- Built-in Flutter animations for the success, gift-opening and level-up moments.

## Web apps (Angular workspace `/web`)

All three apps:
- use the shared projects: `api-client`, `auth`, `design-tokens`, `ui`, `util` (i18n via the built-in `@angular/localize`)
- log in with email + OTP (except the public pages)

1. **`landing`** (SSR, public): see "Landing page". It also hosts:
   - `/g/:token` gift page
   - `/reytinq`
   - `/kampaniya/:slug`
   - `/developers`
   - partner and corporate application forms
2. **`business-portal`** (service providers; roles `BusinessOwner`, `BusinessStaff`, `BrandUser`, partner users):
   - **Kod yoxla / Təhvil ver:**
     - a large single-purpose redemption screen
     - webcam QR, a USB barcode scanner (keyboard-wedge), or manual code
     - preview → select items/amount → confirm → printable/receipt view
     - recent redemptions with a 15-min reverse button
   - dashboard (today, week, month), catalog (groups, prices, images, giftable), offers, askıda entry
   - gifts sold, redemptions, ledger and payouts, CSV export
   - leaderboard rank + "Ən qonaqpərvər biznes" certificate PDF
   - staff and settings (owner only)
   - **İnteqrasiyalar:** clients, webhooks, delivery log, sandbox
   - Brand users: coupon stats and code validation
   - Chains: a business switcher across all businesses linked to the account
3. **`admin`** (roles `Admin`, `CorporateUser`):
   - **Admin:**
     - dashboard
     - categories (dynamic field builder), verifications
     - businesses, users, gifts
     - corporate orders (bank-transfer confirmation), corporates
     - campaigns and pools
     - payments/refunds, payouts
     - partner clients and partner balances (top-up confirmation)
     - Plus
     - reports, audit log
     - leaderboard moderation
     - background jobs page (from `job_runs`)
     - In every list: an "include deleted" toggle and restore, never a permanent delete.
   - **Corporate portal:** gift-order wizard (CSV upload + validation report, payment, progress, report), CSR campaigns and reports.

---

## Work management — GitHub organization, Kanban, epics

### Board and issues
- **One GitHub organization** with the monorepo and **one GitHub Project (v2)**, "QONAQET".
- Kanban columns: `Backlog → Ready → In Progress → In Review → Done`.
- **Epic** = an issue labelled `epic`. **Tasks** are its **sub-issues** (GitHub sub-issues), each with:
  - the label `task` and one `area:*` label (`area:backend`, `area:integration`, `area:mobile`, `area:web-landing`, `area:web-business`, `area:web-admin`, `area:infra`, `area:design`)
  - a priority label `p0`/`p1`/`p2`
- Project fields: `Epic`, `Area`, `Priority`, `Size` (XS/S/M/L), `Status`.
- Issue templates (`.github/ISSUE_TEMPLATE`):
  - **epic**: goal, scope, out of scope, acceptance criteria, task list
  - **task**: context, acceptance criteria, test notes, parent epic
  - **bug**

### How Claude Code works
1. Work **epic by epic, in the order below**. Do not start a new epic while tasks of the current one are open, unless a task is explicitly marked as parallel.
2. Inside an epic, take the top `Ready` task:
   - move it to `In Progress`
   - branch `feat/<issue#>-<slug>` (or `fix/`, `chore/`)
3. Implement it with tests. Then:
   - Conventional Commits that reference the issue (`feat(gifts): partial voucher redemption (#123)`)
   - open a PR with `Closes #123` (template: summary, tests, screenshots for UI)
   - move the task to `In Review`
4. CI must be green: build, tests, lint, architecture tests. Merge with squash. The task moves to `Done` automatically.
5. When all sub-issues of an epic are done:
   - verify the epic's acceptance criteria
   - update `README.md` / `docs/`
   - close the epic
   - tag `epic-Exx-done`
6. If something new comes up, create a new task under the right epic. Never expand scope silently.
7. Architectural decisions that deviate from this file go into `docs/adr/` and need a PR.

**Bootstrap** (run once, when asked):
- With the `gh` CLI, create the labels, the Project with its fields and columns, the issue templates, and all epics below with their tasks as sub-issues, in order.
- Report the created URLs.

### Epics (in order)

| # | Epic | Area | Key tasks (become sub-issues) |
|---|---|---|---|
| E00 | Repo, GitHub automation & dev environment | infra | Monorepo skeleton, docker-compose (all services), repo rulesets, all workflows from "GitHub automation", Dependabot, CODEOWNERS, `dependency-guard` + allowlist, issue/PR templates, bootstrap script, design-token pipeline (Style Dictionary → CSS + Dart) |
| E01 | Backend foundation | backend | Solution + layers, SharedKernel (own dispatcher + behaviors, validation, Result), Mongo soft-delete repository + index runner, EF Core + soft-delete/audit/outbox interceptors, outbox relay + RabbitMQ + inbox, Valkey cache/locks/rate limit, own scheduler, OTel/health, OpenAPI, own architecture tests, own CSV/QR utilities |
| E02 | Identity & users | backend | Email OTP, JWT + refresh rotation, roles and policies, profile, FCM tokens, account soft delete, blocking |
| E03 | Files, email & notifications | backend | S3 storage (`files`, AWSSDK.S3 + RustFS, versioned buckets), email templates (own engine, themed), Playwright PDF renderer, push sender, notification preferences |
| E04 | Businesses & catalog | backend | Onboarding + admin activation, staff memberships, catalog CRUD, settings, geo search, chains (multi-business owners) |
| E05 | Categories & verification | backend | Dynamic categories + auto pool, delete guard, verification wizard API, FIN hash, validity, reminders |
| E06 | Social treating | backend | Offers (daily/suspended), claims with atomic quota, limits, QR/short codes, redemption, expiry job, privacy tests |
| E07 | Payments & ledger | backend | Payriff + Fake, callback + re-check, Postgres payments/refunds, ledger + commission, payouts, reconciliation |
| E08 | Pools, campaigns & donations | backend | Remote/pool/campaign donations, named/anonymous, pool claims, campaign rollover, public stats |
| E09 | Gifting | backend | Gift aggregate, items/voucher/network, link tokens + public API, claim to account, delivery/expiry jobs, partial redemption, cancel/re-issue, refunds/donate on expiry |
| E10 | Corporate & CSR | backend | Corporate accounts, gift orders + CSV validation, bank transfer confirm, fan-out, PDF/CSV reports |
| E11 | Leaderboard & gamification | backend | Score consumer, anti-gaming, Postgres aggregates + Valkey ZSETs, levels, badges job, recompute |
| E12 | QONAQET Plus | backend | Brands, coupons, unique code assignment, brand validation |
| E13 | Integration platform | integration | Integration.Api host, client credentials + scopes, codes/redemptions/reverse, catalog sync, issuer gifts + partner balance, webhooks (signing, retries, redeliver), sandbox, Partner OpenAPI + guide |
| E14 | Mobile foundation | mobile | Project setup, flavors, architecture skeleton, tokens theme, own ApiClient, own plugins (`secure_store`, `code_scanner`, `share_sheet`), auth, routing, push, deep links, QR painter |
| E15 | Mobile user features | mobile | Home, discover/map, business page, Hədiyyə et flow + payment + share, Hədiyyələrim, social Ismarla + claims, verification wizard, Reytinq, Plus, profile |
| E16 | Mobile business mode | mobile | Scan/preview/redeem, manual code, today list, askıda entry, quick stats |
| E17 | Business portal (web) | web-business | Skeleton + auth (now), then — **after the UI guidelines** — redeem screen, dashboard, catalog, offers, sales, ledger/payouts, staff/settings, integrations, brand section |
| E18 | Admin & corporate portal (web) | web-admin | Skeleton + auth (now), then — **after the UI guidelines** — all admin modules, corporate portal |
| E19 | Landing website (web) | web-landing | SSR skeleton + tokens (now), then — **after the UI guidelines** — hero + animations, counters, sections, gift page, reytinq, campaigns, developers, forms, Lighthouse ≥ 90 |
| E20 | Production readiness | infra | Security review (OWASP ASVS L2 checklist), load test (k6) on the redeem + payment paths, backups + restore drills (Mongo, Postgres, RustFS), RustFS version re-evaluation, monitoring/alerts, staging/prod deploy, privacy policy + terms pages |

Epics E14–E16 may run in parallel with E09–E13 once E02 is done. Mark such tasks `parallel-ok`.

## Coding rules

- **No hard deletes** anywhere: Mongo, Postgres or object storage. Never use TTL indexes. Use `SoftDeleteAsync` / `RestoreAsync` only.
- Soft-delete filtering is never bypassed:
  - Mongo: all queries go through the soft-delete repository or query helpers.
  - Postgres: rely on the global filter; `IgnoreQueryFilters()` is allowed only in admin queries.
- Every new index is partial (Mongo `{ isDeleted: false }`, Postgres `WHERE is_deleted = false`) and registered in the index runner or an EF migration.
- Clean Architecture + CQRS:
  - one use case = one folder with command/query, handler and validator
  - Domain has no external dependencies
  - no `MediatR`, no `AutoMapper`, no package outside `docs/dependencies/allowlist.json` (see "Dependency policy")
- Cross-store effects go only through outbox events. Consumers are idempotent (inbox / unique `source_event_id`).
- Angular: follow "Angular UI guidelines". Never install a UI library or invent final screens before that section is filled in.
- Colors and fonts come only from `/design-tokens`. Never hard-code hex values in components.
- Work only through GitHub issues: every PR closes a task, and every task belongs to an epic.
- Never branch on a category code or name. All behavior comes from the category document.
- Keep the core generic, not food-specific. Use names like `catalogItems`, `businesses` and `Voucher`, so the platform can grow to flowers, books, services and other goods.
- C#:
  - nullable enabled, file-scoped namespaces
  - async + `CancellationToken` everywhere
  - no business logic in controllers
- Money is always `long` qəpik, computed only on the server.
- Never log OTPs (except in Development), tokens, gift link tokens, FINs, document contents or card data.
- Never return personal data across sides:
  - recipients' data to businesses, donors or corporates
  - senders' data to businesses
  - Tests must assert this.
- Secrets come only from configuration/environment.
- Every money-moving operation:
  - is idempotent
  - runs in a single-store transaction together with its outbox event (never a distributed transaction)
  - results in a ledger entry and an audit log entry
