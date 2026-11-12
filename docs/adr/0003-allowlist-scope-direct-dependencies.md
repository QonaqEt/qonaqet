# 0003 — Allowlist yalnız birbaşa asılılıqlara tətbiq olunur

- **Status:** Accepted
- **Tarix:** 2026-11-12
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E00-T07 (#8, PR #190), E00-T17 (#200)

## Kontekst

`CLAUDE.md` → "Dependency policy", 4: *"`dependency-guard` fails when a lock file contains a package
that is not on the list."* Lock fayllarında yalnız bizim seçdiyimiz paketlər deyil, onların bütün
asılılıq ağacı olur: Angular CLI workspace-i yüzlərlə npm paketi gətirir, EF Core və OpenTelemetry
onlarla NuGet paketi (`System.*`, `Microsoft.Extensions.*`, `DnsClient`, `Google.Protobuf` …),
Flutter isə `async`, `collection`, `meta` və s. Hamısını `allowlist.json`-a yazmaq:

- hər Dependabot yeniləməsində əl ilə siyahı redaktəsi tələb edir (keçid paketləri tez-tez dəyişir);
- hər birinə ADR + `dependency-approved` tələbini mənasız edir (seçim bizdə deyil);
- siyahını oxunmaz edir və əsl qərarları — *biz nəyi seçdik* — gizlədir.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. Hər paket (keçid daxil) allowlist-də | Mətnə hərfən uyğun | Yüzlərlə giriş, hər yeniləmədə əl işi, ADR tələbi mənasızlaşır |
| B. Yalnız birbaşa asılılıqlar allowlist-də; keçidlər qadağan siyahısı + dependency-review ilə | Qərar verdiyimiz paketlərə nəzarət; avtomatik CVE/lisenziya yoxlaması bütün ağaca | Keçid paketin "single-maintainer" riski avtomatik tutulmur |
| C. B + keçid paketlər üçün publisher/namespace allowlist-i | Ağacın bir hissəsinə əlavə nəzarət | Mürəkkəb, ekosistemlər arası fərqli; fayda/xərc zəif |

## Qərar

**B.**

- `dependency-guard` **birbaşa** asılılıqları (`packages.lock.json` → `Direct`; `package-lock.json` → kök
  və workspace-lərin `dependencies`/`devDependencies`/`optional`/`peer`; `pubspec.lock` → `direct *`,
  SDK paketləri istisna) `allowlist.json`-a qarşı yoxlayır.
- `policy.forbidden_packages` **bütün** ağacda (keçid daxil) yoxlanılır.
- Keçid asılılıqlarının CVE-ləri və GPL/AGPL/SSPL lisenziyaları `actions/dependency-review-action` ilə
  bloklanır (repo public-dir, Code Security tələb olunmur).
- Workflow-larda hər `uses:` allowlist sahibindən olmalı və tam SHA ilə pinlənməlidir.

## Nəticələr

- Müsbət: allowlist bizim qərarlarımızın siyahısı olaraq qalır; Dependabot yeniləmələri əl işi tələb etmir.
- Mənfi / qəbul edilən risk: icazəli paketin gətirdiyi tərk edilmiş/tək-maintainer keçid paketi
  avtomatik tutulmur. Yumşaltma: yeni birbaşa paket üçün ADR-də onun asılılıq ağacına baxılır; Dependabot
  alerts və dependency-review CVE-ləri tutur.
- `CLAUDE.md`-də dəyişən sətir: "Dependency policy", 4-cü bəndin ikinci sətri (bu ADR-ə istinadla).

## Yenidən baxış

Keçid asılılığından qaynaqlanan təhlükəsizlik insidenti olarsa və ya E20 (Production readiness)
təhlükəsizlik baxışında.
