# QONAQET Partner API — inteqrasiya təlimatı

Restoranlar, kassa (POS) sistemləri, çatdırılma tətbiqləri, banklar və digər xidmətlər üçün. Məqsəd: inteqrasiya bir gündə.

- **Baza URL:** client ilə birlikdə verilir (nümunələrdə `https://partner.qonaqet.az`); sandbox və real eyni host-dadır, rejimi client müəyyən edir
- **Prefiks:** `/partner/v1`, token endpoint-i `/oauth/token`
- **Format:** JSON (UTF-8), pul həmişə **qəpik** (`long`, 1 AZN = 100), vaxt UTC ISO 8601
- **OpenAPI:** "QONAQET Partner API v1" — CI-nin `partner-openapi` artefaktı (`partner-v1.json`), Postman/Insomnia-ya birbaşa import
  olunur (bax: [OpenAPI və Postman](#openapi-və-postman))
- **Sandbox:** [partner-sandbox.md](./partner-sandbox.md)

## Kim nə edir

| Partnyor | Tipik istifadə | Scope-lar |
|---|---|---|
| POS / restoran sistemi | Kassada kodu yoxlamaq və təhvil vermək, menyunu sinxronlaşdırmaq | `codes:read`, `redemptions:write`, `catalog:write`, `webhooks` |
| Şəbəkə / franşiza | Bir client, çox biznes (`business_ids`) | eyni |
| Issuer (bank, çatdırılma tətbiqi, HR platforması) | Öz istifadəçilərinə QONAQET hədiyyəsi vermək (ön ödənişli balansdan) | `gifts:issue`, `gifts:read`, `webhooks` |
| Brend (QONAQET Plus) | Kupon kodlarını yoxlamaq | `coupons:validate` |

## Quick start (POS, 5 addım)

1. **Client alın.** Admin (və ya biznes sahibi portalda) sizə `client_id` (`qc_test_…` / `qc_live_…`) və `client_secret` (`qs_…`) verir.
   Secret **bir dəfə** göstərilir — təhlükəsiz yerdə saxlayın.
2. **Token alın:**

   ```bash
   curl -s https://partner.qonaqet.az/oauth/token \
     -d grant_type=client_credentials -d client_id=qc_test_... -d client_secret=qs_... \
     -d "scope=codes:read redemptions:write"
   ```

   ```json
   { "access_token": "eyJhbGciOi...", "token_type": "Bearer", "expires_in": 3600, "scope": "codes:read redemptions:write" }
   ```

3. **Kodu yoxlayın** (QR mətni və ya qısa kod `XXXX-XXXX`):

   ```bash
   curl -s "https://partner.qonaqet.az/partner/v1/codes/K7QM-2XRB?businessId=0196..." -H "Authorization: Bearer $TOKEN"
   ```

   Cavab: növ (hədiyyə / claim / şəbəkə vauçeri), qalan məhsullar və ya məbləğ, müddət və bu biznesdə təhvil oluna bilərmi.
   Alıcının və göndərənin şəxsi məlumatı **heç vaxt** qaytarılmır.

4. **Təhvil verin** (qismən də ola bilər):

   ```bash
   curl -s https://partner.qonaqet.az/partner/v1/redemptions \
     -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: 4c1f0a7e-1" -H "Content-Type: application/json" \
     -d '{ "businessId": "0196...", "code": "K7QM-2XRB", "amountQepik": 1500, "externalReference": "CHECK-1042" }'
   ```

   Məhsul hədiyyəsində məbləğ əvəzinə sətirlər: `"lines": [{ "catalogItemId": "0196...", "qty": 1 }]`.
   Cavabda `redemptionId` və qalıq var.

5. **Səhv olubsa geri alın** — 15 dəqiqə ərzində, yalnız öz təhvilinizi:

   ```bash
   curl -s -X POST https://partner.qonaqet.az/partner/v1/redemptions/0196.../reverse \
     -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: 4c1f0a7e-2"
   ```

## Autentifikasiya

- OAuth2 **client credentials** (RFC 6749 §4.4): `POST /oauth/token`, `application/x-www-form-urlencoded`, sahələr
  `grant_type=client_credentials`, `client_id`, `client_secret`, istəyə görə `scope` (boşluqla ayrılmış; boşdursa client-in bütün scope-ları).
- Token JWT-dir, **1 saat** etibarlıdır; müddət bitəndə yenisini alın (refresh token yoxdur).
- **Secret rotasiyası:** yeni secret verilir, köhnəsi **24 saat** də işləyir — sisteminizi dayandırmadan dəyişin.
- **Ləğv** olunmuş client-in token-ləri dərhal işləməz olur (`invalid_client`).
- Hər client-in dəqiqəlik sorğu limiti var (default 120); aşıldıqda `429` və `Retry-After`.

## Idempotency

Hər `POST` sorğusunda (`/oauth/token`-dan başqa) `Idempotency-Key` başlığı **məcburidir** (≤ 200 simvol, məsələn UUID):

| Vəziyyət | Nəticə |
|---|---|
| Başlıq yoxdur | `400 idempotency_key_required` |
| Eyni açar + eyni body | ilk cavab olduğu kimi təkrar qaytarılır (pul ikinci dəfə hərəkət etmir) |
| Eyni açar + fərqli body | `409 idempotency_key_reused` |

Şəbəkə xətasından sonra **eyni açarla** təkrar göndərin.

## Səhvlər

Səhvlər [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) formatındadır, sabit `code` sahəsi ilə:

```json
{ "status": 409, "title": "Conflict", "code": "gift_insufficient_balance", "detail": "Hədiyyənin qalığı kifayət etmir." }
```

Tez-tez rast gəlinənlər: `invalid_client`, `invalid_scope`, `business_not_allowed`, `sandbox_mismatch`, `code_not_found`,
`already_redeemed`, `gift_expired`, `gift_insufficient_balance`, `gift_changed` (eyni anda başqa təhvil — yenidən cəhd edin),
`network_voucher_not_accepted`, `reverse_window_passed`, `already_reversed`, `insufficient_partner_balance`, `webhook_url_invalid`.

## Kodlar və təhvil

- `GET /partner/v1/codes/{code}?businessId=` — baxış, heç nə dəyişmir.
- `POST /partner/v1/redemptions` — `{ businessId, code, lines[] | amountQepik, externalReference? }`. Qaydalar kassadakı işçi ilə
  eynidir: qismən təhvil, şəbəkə vauçeri yalnız `acceptsNetworkVouchers` olan biznesdə, claim kodu birdəfəlikdir.
- `POST /partner/v1/redemptions/{id}/reverse` — 15 dəqiqə ərzində; qalıq bərpa olunur, ledger və xal tərs yazılır.
- Client yalnız öz `business_ids`-indəki bizneslərə müraciət edir (`business_not_allowed`).

## Kataloq sinxronu

```http
PUT /partner/v1/catalog/{businessId}?prune=true
Authorization: Bearer <token>
Content-Type: application/json

{ "items": [
  { "externalId": "SKU-001", "title": "Americano", "priceQepik": 450, "group": "İsti içkilər", "isActive": true, "isGiftable": true }
] }
```

- `externalId` üzrə upsert (≤ 1000 məhsul). `prune=true` olanda sorğuda olmayan **sinxron** məhsullar soft delete olunur (portalda əl ilə
  əlavə olunanlara toxunulmur). Cavab: `created`, `updated`, `unchanged`, `pruned`.
- `GET /partner/v1/catalog/{businessId}` — cari kataloq.

## Issuer hədiyyələri və balans

```http
POST /partner/v1/gifts
Idempotency-Key: order-77812

{
  "what": { "kind": "Voucher", "scope": "Network", "amountQepik": 2000 },
  "recipientName": "Aysel",
  "recipientEmail": "aysel@example.az",
  "theme": "Thanks",
  "message": "Sifarişiniz üçün təşəkkür!",
  "externalReference": "order-77812"
}
```

- Məbləğ ön ödənişli **partnyor balansından** silinir (`PartnerIssue`); balans çatmırsa `insufficient_partner_balance`.
- `recipientEmail` boşdursa cavabdakı `link`-i (bir dəfə göstərilir) istifadəçinizə özünüz göndərin.
- `GET /partner/v1/gifts/{id}`, `POST /partner/v1/gifts/{id}/cancel` (yalnız çatdırılmamış; məbləğ balansa qayıdır).
- İstifadə olunmamış qalıq müddət bitəndə balansa qayıdır.
- `GET /partner/v1/balance`, `GET /partner/v1/ledger`; balans artımı: bank köçürməsi (admin təsdiqləyir) və ya kart
  (`POST /partner/v1/balance/top-ups` → `paymentUrl`).

## Webhook-lar

### Abunə

```http
POST /partner/v1/webhooks
Idempotency-Key: wh-1

{ "url": "https://pos.example.az/qonaqet/webhooks", "eventTypes": ["redemption.created", "redemption.reversed"] }
```

Cavabda `secret` (`whsec_…`) **bir dəfə** göstərilir. URL HTTPS olmalıdır (sandbox-da HTTP da olar). Client başına ən çox 10 abunə.
`GET /partner/v1/webhooks`, `DELETE /partner/v1/webhooks/{id}` (soft delete).

### Hadisələr

| Hadisə | Kimə | `data` |
|---|---|---|
| `redemption.created`, `redemption.reversed` | təhvilin olduğu biznesə bağlı client-lər | `redemptionId`, `businessId`, `kind` (`Gift`/`Claim`), `valueQepik` |
| `gift.created`, `gift.delivered`, `gift.redeemed`, `gift.expired` | hədiyyəni buraxan issuer | `giftId`, `externalReference`, `status`, `amountQepik`, `remainingQepik`, `redeemedNowQepik`, `expiresAt` |
| `payout.created` | biznesə bağlı client-lər | `payoutId`, `businessId`, `amountQepik` |
| `catalog.synced` | biznesə bağlı client-lər | `businessId`, `created`, `updated`, `unchanged`, `pruned` |
| `partner.balance.low` | client-in özü | `balanceQepik`, `thresholdQepik` |

Body (versiyalanıb, `v1`):

```json
{
  "id": "0196f1c2-7a3e-7b10-9c55-2f0d1e6b8a41",
  "type": "redemption.created",
  "apiVersion": "v1",
  "createdAt": "2026-11-26T09:15:02+00:00",
  "data": { "redemptionId": "0196...", "businessId": "0196...", "kind": "Gift", "valueQepik": 1500 }
}
```

`id` eyni hadisənin təkrar çatdırılmasında dəyişmir — dublikatları onunla süzün.

### Başlıqlar

| Başlıq | Məna |
|---|---|
| `X-Qonaqet-Event` | hadisə növü |
| `X-Qonaqet-Delivery` | çatdırılmanın sabit ID-si (yenidən cəhdlərdə eyni) |
| `X-Qonaqet-Timestamp` | Unix vaxtı (saniyə) |
| `X-Qonaqet-Signature` | `sha256=` + hex(HMAC-SHA256(secret, timestamp + "." + body)) |

### Cavab, yenidən cəhd, redeliver

- `2xx` = qəbul edildi. Digər status, 10 saniyəlik timeout və şəbəkə xətası uğursuz cəhd sayılır.
- Yenidən cəhdlər: **1 d, 5 d, 30 d, 2 s, 12 s, 24 s**; bundan sonra çatdırılma `Failed` olur.
- `GET /partner/v1/webhooks/deliveries` — çatdırılma jurnalı (status, cəhd sayı, son cavab kodu);
  `POST /partner/v1/webhooks/deliveries/{deliveryId}/redeliver` — dərhal yenidən göndərmə.

### İmzanın yoxlanması

Qaydalar: imzanı **xam body** üzərində hesablayın (JSON-u parse edib yenidən serializasiya etməyin), müqayisəni sabit vaxtlı edin və
**5 dəqiqədən köhnə** `X-Qonaqet-Timestamp`-li sorğunu rədd edin.

#### Test vektoru

| | |
|---|---|
| secret | `whsec_test` |
| timestamp | `1700000000` |
| body | `{"a":1}` |
| imza | `sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789` |

Aşağıdakı nümunələr [`integration-samples/`](./integration-samples/) qovluğundadır; hər biri bu vektorla özünü yoxlayır (`ok` çap edir):
`dotnet run webhook-verify.cs`, `node webhook-verify.js`, `php webhook-verify.php`.

#### C#

```csharp
using System.Security.Cryptography;
using System.Text;

static bool Verify(string secret, string timestamp, string body, string signatureHeader, DateTimeOffset now)
{
    if (!long.TryParse(timestamp, out var ts) || Math.Abs(now.ToUnixTimeSeconds() - ts) > 300)
    {
        return false;
    }

    var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
    var expected = Encoding.ASCII.GetBytes("sha256=" + Convert.ToHexStringLower(mac));
    return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signatureHeader ?? string.Empty));
}
```

ASP.NET Core-da xam body: `Request.EnableBuffering()` + `await new StreamReader(Request.Body).ReadToEndAsync()`.

#### JavaScript (Node.js)

```js
const crypto = require('node:crypto');

function verifyQonaqetWebhook(secret, timestamp, rawBody, signatureHeader, nowSeconds = Math.floor(Date.now() / 1000)) {
  const ts = Number(timestamp);
  if (!Number.isInteger(ts) || Math.abs(nowSeconds - ts) > 300) return false;
  const expected = Buffer.from('sha256=' + crypto.createHmac('sha256', secret).update(`${timestamp}.${rawBody}`).digest('hex'));
  const actual = Buffer.from(signatureHeader || '');
  return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
}

// Express: app.post('/qonaqet/webhooks', express.raw({ type: 'application/json' }), (req, res) => {
//   const ok = verifyQonaqetWebhook(process.env.QONAQET_WEBHOOK_SECRET, req.get('X-Qonaqet-Timestamp'),
//     req.body.toString('utf8'), req.get('X-Qonaqet-Signature'));
//   res.sendStatus(ok ? 200 : 401);
// });
```

#### PHP

```php
function qonaqet_verify_webhook(string $secret, string $timestamp, string $rawBody, string $signatureHeader, ?int $now = null): bool
{
    $now = $now ?? time();
    if (!ctype_digit($timestamp) || abs($now - (int) $timestamp) > 300) {
        return false;
    }

    $expected = 'sha256=' . hash_hmac('sha256', $timestamp . '.' . $rawBody, $secret);
    return hash_equals($expected, $signatureHeader);
}

// $ok = qonaqet_verify_webhook(getenv('QONAQET_WEBHOOK_SECRET'), $_SERVER['HTTP_X_QONAQET_TIMESTAMP'] ?? '',
//     file_get_contents('php://input'), $_SERVER['HTTP_X_QONAQET_SIGNATURE'] ?? '');
// http_response_code($ok ? 200 : 401);
```

## Məxfilik

Partnyor cavablarında və webhook-larda şəxsi məlumat yoxdur: alıcının/göndərənin adı, emaili, kateqoriyası heç vaxt qaytarılmır.
Yeganə istisna partnyorun özünün göndərdiyi sahələrdir (`externalReference`, issuer hədiyyəsinin alıcısı).

## OpenAPI və Postman

- Hər backend CI işində `partner-openapi` artefaktı (`partner-v1.json`, OpenAPI 3.1) dərc olunur: GitHub → Actions → `ci-backend` →
  son uğurlu iş → **Artifacts**. Development mühitində eyni sənəd `GET /openapi/partner-v1.json`-dadır.
- **Postman:** Import → File → `partner-v1.json`. Kolleksiyanın **Authorization** bölməsində `OAuth 2.0`, grant type
  `Client Credentials` artıq sənəddən gəlir; `Access Token URL`-i baza URL ilə tamamlayın, `client_id`/`client_secret` yazın.
- **Insomnia:** Create → Import → `partner-v1.json`; mühitdə `base_url` təyin edin.
- Hər `POST` əməliyyatında `Idempotency-Key` başlığı sənəddə məcburi parametr kimi görünür.
