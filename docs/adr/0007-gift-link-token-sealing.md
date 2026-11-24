# 0007 — Hədiyyə link token-inin müvəqqəti şifrəli nüsxəsi

- **Status:** Accepted (repo sahibi, 2026-11-24)
- **Tarix:** 2026-11-21
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E09-T02, E09-T03, E09-T05

## Kontekst

`CLAUDE.md`: link token-i 32 təsadüfi baytdır (base64url), **heç vaxt saxlanılmır** — yalnız `linkTokenHash`. Eyni zamanda:

- token ödəniş təsdiqində yaradılır və göndərənə **bir dəfə** qaytarılmalıdır, amma təsdiq asinxron consumer-dədir (göndərən cavabı gözləmir);
- çatdırılma job-u `deliverAt` gələndə (90 günə qədər sonra) alıcıya **eyni** linki email ilə göndərməlidir.

Yalnız hash saxlansa, nə göndərən sonradan linki götürə bilər, nə də job email-ə link qoya bilər.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. Token-i HMAC ilə ID-dən törətmək | Heç nə saxlanılmır | Token təsadüfi deyil; açar sızsa bütün aktiv linklər açılır |
| B. Hər çatdırılmada/göndərəndə yeni token | Saxlanılmır | Email-dəki və göndərənin linki fərqli olur, biri digərini etibarsız edir |
| C. Təsadüfi token + hash + **müvəqqəti** AES-GCM nüsxəsi | Token təsadüfi qalır; email və göndərən eyni linki alır | Açar + DB birlikdə sızsa, hələ göndərilməmiş linklər açıla bilər |

## Qərar

**C.** `linkTokenHash` (HMAC, axtarış) daimi saxlanılır. `linkTokenSealed` (AES-256-GCM, açar `Security:GiftLinkSealSecret`)
yalnız lazım olduqca saxlanılır: çatdırılma emaili hələ getməyibsə və ya göndərən linki hələ götürməyibsə. Hər iki addım
bitəndə, eləcə də ləğv və müddət bitməsində aqreqat nüsxəni silir (`null`). Link yenidən veriləndə yeni token cavabda
qaytarılır və nüsxə yalnız email hələ gözlənilirsə qalır.

## Nəticələr

- Açıq token heç vaxt DB-də, logda, outbox-da və ya push-da olmur; yalnız göndərənin cavabında və alıcının emailində.
- Yeni sirr: `Security__GiftLinkSealSecret` (compose və `.env.example`-da). Açar dəyişsə, hələ çatdırılmamış hədiyyələrin
  linki yenidən verilməlidir (`reissue-link`).
