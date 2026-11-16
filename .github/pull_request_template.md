## Xülasə

<!-- Nə dəyişdi və niyə. Bir-iki cümlə. -->

<!-- docs/epics-dəki task açarı; bir neçə ola bilər: Task: E01-T03, E01-T04 -->
Task: Exx-Tnn

## Testlər

<!-- Hansı testlər əlavə olundu/dəyişdi və nə sübut edirlər. -->

- [ ] Vahid testlər
- [ ] Integration testlər (Testcontainers)
- [ ] Arxitektura testləri keçir

## Skrinşotlar

<!-- UI dəyişikliyi varsa məcburidir. Yoxdursa "n/a" yaz. -->

## Yoxlama siyahısı

- [ ] Task-ın epic-i məndədir (`scripts/epic.py status`); task statusu md-də yeniləndi və `docs/BOARD.md` yenidən yaradıldı
- [ ] Başlıq Conventional Commit formatındadır
- [ ] Hard delete yoxdur (Mongo, Postgres, obyekt saxlama)
- [ ] Yeni indekslər partial-dır və index runner-ə / EF migrasiyasına qeyd olunub
- [ ] Pul `long` qəpikdir və yalnız serverdə hesablanır
- [ ] Kateqoriya kodu/adı üzrə şərt yoxdur
- [ ] Tərəflər arası şəxsi məlumat sızmır; testlər bunu yoxlayır
- [ ] OTP, token, FIN, kart məlumatı loglanmır
- [ ] Yeni paket əlavə olunubsa: ADR + `allowlist.json` + `dependency-approved` label-ı var
- [ ] Rənglər/şriftlər `/design-tokens`-dan gəlir, hard-code hex yoxdur
