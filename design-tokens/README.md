# design-tokens

`tokens.json` — rəng və şriftlərin **yeganə mənbəyi** (`CLAUDE.md` → "Brand, colors and the landing page").
Komponentlərdə hex və şrift adı yazılmır; yalnız buradan generasiya olunan dəyişənlər işlədilir.

```bash
cd design-tokens
npm ci
npm run build      # CSS + Dart yaradır
npm run contrast   # WCAG kontrastı (AA məcburidir)
npm test
```

| Çıxış | Yer | İstifadə |
|---|---|---|
| CSS dəyişənləri | `web/projects/design-tokens/src/lib/generated/tokens.css` | `var(--qn-color-brand-nar)` |
| Dart sinfi | `mobile/lib/app/theme/generated/qonaqet_tokens.dart` | `QonaqetTokens.colorBrandNar` |

Generasiya olunan fayllar git-ə düşmür (`.gitignore`); CI (`ci-infra / tokens`) hər dəfə yenidən yaradır.

**Kontrast qeydi:** `cream` mətn `nar` fon üzərində 3.62:1-dir — yalnız iri/qalın mətn üçün (AA-large) keçir.
Adi ölçülü mətnli CTA üçün `plum` (4.18:1) işlət. Cütlər `contrast-pairs.json`-dadır.

**Hədiyyə temalarının rəngləri** (`color.theme.*`) təklifdir — UI həmkarı təsdiqləyənə qədər.
