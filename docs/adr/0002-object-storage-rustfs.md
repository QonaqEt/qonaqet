# 0002 — Obyekt saxlama: RustFS

- **Status:** Accepted (dependency policy-yə müddətli istisna)
- **Tarix:** 2026-11-11
- **Qərar verən:** repo sahibi
- **Yenidən baxış:** 6 ayda bir — növbəti: 2027-05-11 (task `E20-T04`)

## Kontekst

Fayllar (təsdiq sənədləri, loqolar, banner-lər, kataloq şəkilləri, CSV-lər) S3 uyğun obyekt
saxlamada yerləşir. `CLAUDE.md` → "Dependency policy" **tək-tərtibatçılı və ya yetkin olmayan**
layihələri qadağan edir, həmçinin **MinIO**-nu (Fevral 2026-da arxivləşdirilib) açıq şəkildə
qadağan edir.

Buna baxmayaraq `CLAUDE.md` → Infrastructure cədvəli obyekt saxlama kimi **RustFS**-i seçir.
RustFS gənc layihədir (GA Sentyabr 2026), yəni formal olaraq siyasətə ziddir. Bu ADR həmin
ziddiyyəti açıq şəkildə qeydə alır.

## Nəzərdən keçirilən variantlar

| Variant | Üstünlük | Çatışmazlıq |
|---|---|---|
| **MinIO** | Geniş yayılmış, tanış | Arxivləşdirilib; AGPL/lisenziya riski — siyasətlə qadağandır |
| **Ceph RGW** | Yetkin, sübut olunmuş | Tək node üçün ağır; əməliyyat yükü böyükdür |
| **İdarə olunan S3** (AWS/Cloudflare R2 və s.) | Əməliyyat yükü yoxdur | Xarici asılılıq, xərc, məlumatın ölkədən kənarda saxlanması |
| **RustFS** | S3 uyğun, yüngül, Apache-2.0, tək node üçün uyğun | Gənc layihə; uzunmüddətli dayanıqlığı sübut olunmayıb |

## Qərar

RustFS seçilir, lakin **müddətli istisna** kimi:

- Yalnız **GA versiyasına pinlənir** (`rustfs/rustfs:1.0.x`); `latest` və preview tag-ları işlədilmir
- **Distributed rejim** və preview funksiyaları (məs. MinIO on-disk uyğunluğu) **işlədilmir**
- Production: tək node + gündəlik backup (ikinci diskə/host-a replikasiya və ya gecə off-site S3 sinxronu)
- Integration testlər RustFS-i Testcontainers generic container ilə, pinlənmiş image ilə qaldırır

Risk bu şəkildə məhdudlaşdırılır: kod **yalnız S3 API-si** ilə danışır (`IFileStorage` arxasında
`AWSSDK.S3`, `ForcePathStyle = true`). Ona görə saxlama təchizatçısının dəyişdirilməsi **konfiqurasiya
dəyişikliyidir**, kod dəyişikliyi deyil.

**Fallback:** Ceph RGW və ya idarə olunan S3.

## Nəticələr

- Müsbət: yüngül, vendor-neytral, Apache-2.0; dev/staging/prod eyni API ilə işləyir
- Mənfi / qəbul edilən risk: layihə gəncdir; tərk edilmə və ya ciddi zəiflik riski var
- Bu risk **6 aylıq yenidən baxış** və **tez keçid imkanı** (S3 API) ilə idarə olunur
- `CLAUDE.md` → Infrastructure cədvəli bu istisnaya artıq istinad edir

## Yenidən baxış

Hər 6 ayda (`E20-T04` task-ı):

- [ ] Cari GA versiyası və buraxılış tezliyi
- [ ] Açıq CVE-lər və təhlükəsizlik xəbərdarlıqları
- [ ] Layihənin fəallığı (commit-lər, tərtibatçı sayı, idarəetmə)
- [ ] Qərar: davam / alternativə keçid — ADR statusu uyğun yenilənir
