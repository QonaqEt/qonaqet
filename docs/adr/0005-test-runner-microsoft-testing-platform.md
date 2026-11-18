# 0005 — Test runner: Microsoft.Testing.Platform + Microsoft.Testing.Extensions.CodeCoverage

- **Status:** Proposed
- **Tarix:** 2026-11-18
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E01-T01, E00-T04 (`ci-backend.yml`)

## Kontekst

`CLAUDE.md` test üçün **xUnit v3**-ü seçir, `ci-backend.yml` isə coverage xülasəsi üçün cobertura hesabatı gözləyir.
xUnit v3 test layihələri öz icra faylıdır və iki runner ilə işləyə bilər:

- **VSTest** — `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` paketlərini tələb edir; coverage
  `--collect "Code Coverage"` ilə `Microsoft.CodeCoverage` (keçid asılılıq) üzərindən gəlir.
- **Microsoft.Testing.Platform (MTP)** — `xunit.v3` paketinə daxildir, .NET 10 SDK-da `global.json` →
  `test.runner` ilə `dotnet test`-in standart rejimidir; coverage üçün `Microsoft.Testing.Extensions.CodeCoverage` lazımdır.

Hər iki halda allowlist-də olmayan ən azı bir **birbaşa** asılılıq əlavə olunmalıdır.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. VSTest | Köhnə `--collect` sintaksisi dəyişmir | İki əlavə birbaşa paket (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`); VSTest Microsoft-un köhnələn yoludur |
| B. MTP + `Microsoft.Testing.Extensions.CodeCoverage` | Bir əlavə paket, hər ikisi Microsoft; .NET 10 standartı; daha sürətli | CI-də `dotnet test --project … --coverage` sintaksisi |
| C. Coverage-siz | Əlavə paket yoxdur | E00-T04-ün coverage xülasəsi işləməz |

## Qərar

**B.** `backend/global.json` → `"test": { "runner": "Microsoft.Testing.Platform" }`. Test layihələri
`tests/Directory.Build.props` vasitəsilə `xunit.v3` və `Microsoft.Testing.Extensions.CodeCoverage`-ə istinad edir.

Lisenziya: paket **Microsoft .NET Library** lisenziyası ilədir (pulsuz, açıq mənbə deyil). Yalnız test
layihələrində işlədilir, heç bir host-a və ya imic-ə daxil olmur. VSTest yolu da eyni lisenziyalı
`Microsoft.CodeCoverage` paketini gətirir, yəni risk profili dəyişmir. GPL/AGPL/SSPL deyil.

## Nəticələr

- Müsbət: bir paket, platforma satıcısından; `dotnet test` .NET 10 standart rejimində.
- Mənfi / qəbul edilən risk: açıq mənbə olmayan test aləti (yalnız CI/dev-də).
- `CLAUDE.md`-də dəyişməli olan sətir(lər): yoxdur (stack cədvəlindəki "xUnit v3" qüvvədədir).

## Yenidən baxış

Microsoft coverage-i açıq lisenziyaya keçirsə və ya xUnit v3 MTP dəstəyini dayandırsa.
