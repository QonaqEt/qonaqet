# 0006 — EF Core migrasiyaları əl ilə yazılır (Design paketi olmadan)

- **Status:** Proposed
- **Tarix:** 2026-11-19
- **Qərar verən:** repo sahibi
- **Əlaqəli:** E01-T08

## Kontekst

`dotnet ef migrations add` `Microsoft.EntityFrameworkCore.Design` paketini (və `dotnet-ef` alətini) tələb edir.
Hər ikisi Microsoft-dandır, amma `docs/dependencies/allowlist.json`-da yoxdur; əlavə etmək ADR + `dependency-approved`
label-ı tələb edir. E01-T08 ilk migrasiyanı gətirməlidir və sahibin label-ını gözləmək işi bloklayır.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| A. `Microsoft.EntityFrameworkCore.Design` + `dotnet-ef` | Migrasiya və model snapshot avtomatik yaranır | Yeni birbaşa asılılıq + yerli alət; sahibin təsdiqi lazımdır |
| B. Əl ilə yazılmış `Migration` sinifləri, snapshot-suz | Heç bir yeni paket yoxdur; SQL tam nəzarətdədir | Model snapshot yoxdur → EF-in "pending model changes" yoxlaması işləmir |
| C. `EnsureCreated` | Ən sadə | Sxemin təkamülü yoxdur, production üçün yararsızdır |

## Qərar

**B.** Migrasiyalar `Persistence/Postgres/Migrations/` altında `[DbContext]` + `[Migration("yyyyMMddHHmmss_Ad")]`
atributları ilə əl ilə yazılır; `Down()` yazılmır (məlumat heç vaxt silinmir — geri dönüş yeni irəli migrasiyadır).
`PendingModelChangesWarning` söndürülür, onun əvəzinə `PostgresStoreTests.Migration_matches_the_ef_model_column_by_column` (Testcontainers) miqrasiya edilmiş
sxemi EF modeli ilə **sütun-sütun** (ad, tip, uzunluq, nullability) və indeks adları üzrə müqayisə edir.

## Nəticələr

- Model dəyişikliyi migrasiyasız birləşə bilməz: integration testi qırılır.
- Sahib Design paketini təsdiqləsə, A-ya keçid sadədir: mövcud migrasiyalar qalır, ilk generasiya snapshot yaradır.
- Yenidən baxış: Design paketi allowlist-ə düşəndə.
