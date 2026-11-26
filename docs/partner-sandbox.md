# Partner API — sandbox

Sandbox inteqrasiyanı real pul və real müştərilərə toxunmadan sınamaq üçündür (E13-T09).

## Necə işləyir

| Nə | Sandbox | Real (live) |
|---|---|---|
| Client ID | `qc_test_…` | `qc_live_…` |
| Biznes | yalnız admin-in sandbox kimi işarələdiyi bizneslər (`PUT /api/v1/admin/businesses/{id}/sandbox`) | yalnız real bizneslər |
| Ödəniş (balans artımı) | Fake gateway | Payriff |
| Ledger | sandbox hədiyyələrinin təhvili və geri alınması **yazılmır** | yazılır |
| Axtarış, reytinq | sandbox biznesi görünmür | görünür |

Qaydalar:

- Sandbox client yalnız sandbox biznesə bağlana bilər; real client yalnız real biznesə. Qarışıq sorğu `sandbox_mismatch` qaytarır.
- Sandbox hədiyyəsinin kodu real biznesdə (kassada və ya API ilə) keçmir: `sandbox_mismatch`.
- Sandbox issuer-in hədiyyəsi (`POST /partner/v1/gifts`) yalnız sandbox biznesə və ya şəbəkə vauçeri kimi yalnız sandbox bizneslərdə işləyir.

## Test kodları

Kassa/POS inteqrasiyasını sınamaq üçün təhvilə hazır test vauçeri yaradın (scope `codes:read`, yalnız sandbox client):

```http
POST /partner/v1/sandbox/test-codes
Authorization: Bearer <token>
Idempotency-Key: 7f1c…
Content-Type: application/json

{ "businessId": "<sandbox biznesin id-si>", "amountQepik": 3000 }
```

Cavab (`201 Created`):

```json
{
  "giftId": "0196…",
  "businessId": "0196…",
  "amountQepik": 3000,
  "shortCode": "K7QM-2XRB",
  "qrPayload": "Zy4wMTk2…",
  "expiresAt": "2027-01-25T08:00:00+00:00"
}
```

- `amountQepik` boş qala bilər (default 50 AZN); ən çox 200 AZN.
- `shortCode` kassada əl ilə yazılan koddur, `qrPayload` skanerin oxuduğu mətndir. Hər ikisi `GET /partner/v1/codes/{code}` və
  `POST /partner/v1/redemptions`-da işləyir; qismən təhvil və 15 dəqiqəlik `reverse` real rejimdəki kimidir.
- Test vauçeri heç bir balansdan ödənilmir və müddəti bitəndə heç nə qaytarılmır.

## Tipik ssenari

1. Admin sizə sandbox client (`qc_test_…`) və sandbox biznes verir.
2. `POST /oauth/token` ilə token alın.
3. `POST /partner/v1/sandbox/test-codes` ilə test kodu yaradın.
4. `GET /partner/v1/codes/{shortCode}?businessId=…` ilə yoxlayın, `POST /partner/v1/redemptions` ilə təhvil verin.
5. İstəsəniz `POST /partner/v1/webhooks` ilə abunə olub `redemption.created` webhook-unu qəbul edin.
