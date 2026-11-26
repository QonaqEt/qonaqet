# 0008 — Webhook imza secret-i hash yox, şifrəli saxlanılır

- **Status:** Proposed
- **Tarix:** 2026-11-26
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E13-T08, ADR 0007

## Kontekst

`CLAUDE.md` → `webhook_subscriptions`: `secret_hash`. Amma webhook-u imzalamaq üçün server hər göndərişdə
`HMAC(secret, timestamp + "." + body)` hesablamalıdır — bunun üçün **açıq** secret lazımdır. Hash-dən secret bərpa olunmur,
yəni yalnız hash saxlasaq imza qurmaq mümkün deyil (client secret-dən fərqli olaraq, burada server özü imzalayan tərəfdir).

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. Hash-i imza açarı kimi istifadə etmək (partnyora hash-i vermək) | Sxem dəyişmir | Faktiki olaraq açıq secret saxlanılır; ad aldadıcıdır |
| B. Secret-i açıq saxlamaq | Sadə | DB sızsa bütün partnyorların imzası saxtalaşdırıla bilər |
| C. Secret-i server açarı ilə AES-256-GCM-lə şifrələmək (`secret_sealed`) | Yalnız DB sızması kifayət etmir | Açar konfiqurasiyada saxlanılmalıdır |

## Qərar

**C.** Sütun `secret_sealed` (`secret_hash` əvəzinə). `ISecretSealer` (`AesSecretSealer`): açar =
HMAC-SHA256(`Security:GiftLinkSealSecret`, `"seal:" + məqsəd`) — ADR 0007-dəki sirdən məqsəd üzrə ayrıca açar törənir,
yeni konfiqurasiya sirri lazım deyil. Secret (`whsec_…`) partnyora yalnız yaradılma cavabında bir dəfə göstərilir.

## Nəticələr

- Açıq secret heç vaxt DB-də, logda və ya API cavabında (yaradılmadan başqa) olmur.
- `Security:GiftLinkSealSecret` dəyişsə, mövcud abunələrin çatdırılmaları `Failed` (`subscription_unavailable`) olur;
  partnyor abunəni yenidən yaratmalıdır.
