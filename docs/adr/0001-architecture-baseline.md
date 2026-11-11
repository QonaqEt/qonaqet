# 0001 — Arxitektura bazisi `CLAUDE.md`-dir

- **Status:** Accepted
- **Tarix:** 2026-11-11
- **Qərar verən:** repo sahibi

## Kontekst

Layihənin arxitekturası, texnoloji stack-i, domen modeli, biznes qaydaları, asılılıq siyasəti və iş
prosesi bir sənəddə — `CLAUDE.md`-də — toplanıb. Bu sənəd həm insan, həm də Claude Code üçün
işləmə qaydasıdır.

Sual: ADR-lər bu sənədi təkrarlamalıdır, yoxsa ona istinad etməlidir?

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| Hər qərarı ADR-ə köçürmək | Klassik ADR praktikası | İki mənbə bir-birindən ayrılır, ziddiyyət yaranır |
| `CLAUDE.md` yeganə mənbə, ADR yalnız **kənaraçıxma** üçün | Bir həqiqət mənbəyi; ADR-lər az və mənalı qalır | `CLAUDE.md` böyükdür və diqqətlə oxunmalıdır |

## Qərar

`CLAUDE.md` yeganə həqiqət mənbəyidir. ADR yalnız o sənədin qoyduğu qaydadan **kənara çıxan**
və ya ona **istisna** olan qərarlar üçün yazılır.

Bura aiddir:

- `docs/dependencies/allowlist.json`-a yeni paket əlavə etmək
- "Tech stack (fixed — do not substitute)" cədvəlindən kənara çıxmaq
- Soft delete, outbox, pul vahidi və məxfilik qaydalarına istisna
- Müddətli istisnalar (bax: ADR 0002)

Bura aid **deyil**: adi dizayn seçimləri, adlandırma, refaktorinq — bunlar PR təsvirində izah olunur.

## Nəticələr

- Müsbət: qaydalar bir yerdə qalır; ADR qovluğu səs-küy yığmır
- Mənfi: `CLAUDE.md` böyüyür və onun redaktəsi `CODEOWNERS` ilə qorunmalıdır (qorunur)
- Qərar `CLAUDE.md`-i dəyişdirirsə, ADR və sənəd dəyişikliyi **eyni PR-da** gedir

## Yenidən baxış

Lazım deyil.
