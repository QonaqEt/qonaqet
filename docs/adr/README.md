# Architecture Decision Records

`CLAUDE.md`-dən kənara çıxan və ya onun qoyduğu qaydaya istisna olan hər qərar burada yazılır və
PR ilə təsdiqlənir (`CLAUDE.md` → "How Claude Code works", 7).

- Fayl adı: `NNNN-qisa-basliq.md` (ardıcıl nömrə)
- Şablon: [`0000-template.md`](./0000-template.md)
- Status: `Proposed` → `Accepted` / `Rejected` → sonradan `Superseded by NNNN`
- Qəbul edilmiş ADR **silinmir**; əvəz olunanda statusu dəyişir və yenisinə istinad verilir

## Siyahı

| # | Başlıq | Status | Yenidən baxış |
|---|---|---|---|
| [0001](./0001-architecture-baseline.md) | Arxitektura bazisi `CLAUDE.md`-dir | Accepted | — |
| [0002](./0002-object-storage-rustfs.md) | Obyekt saxlama: RustFS | Accepted (istisna) | 6 ayda bir |
| [0003](./0003-allowlist-scope-direct-dependencies.md) | Allowlist yalnız birbaşa asılılıqlara tətbiq olunur | Accepted | E20 təhlükəsizlik baxışında |
| [0004](./0004-work-tracking-in-markdown.md) | İş izləmə repo-dakı markdown-dadır | Accepted | komanda böyüyəndə |
| [0005](./0005-test-runner-microsoft-testing-platform.md) | Test runner: Microsoft.Testing.Platform + CodeCoverage | Proposed | coverage lisenziyası dəyişəndə |
| [0006](./0006-hand-written-ef-migrations.md) | EF Core migrasiyaları əl ilə yazılır | Proposed | Design paketi allowlist-ə düşəndə |
| [0007](./0007-gift-link-token-sealing.md) | Hədiyyə link token-inin müvəqqəti şifrəli nüsxəsi | Proposed | — |
