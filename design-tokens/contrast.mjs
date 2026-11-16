// WCAG 2.x kontrast yoxlaması — öz kodumuz, asılılıqsız (CLAUDE.md → Dependency policy, 2).
// contrast-pairs.json-dakı hər cüt üçün nisbəti hesablayır; tələb ödənmirsə 1 ilə çıxır.
// Əlavə qayda: saffron üzərində mətn həmişə plum-dur (CLAUDE.md → Brand).
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
export const LEVELS = { AA: 4.5, 'AA-large': 3, AAA: 7 };

export function lookup(tokens, path, seen = new Set()) {
  if (seen.has(path)) throw new Error(`token istinadında dövr: ${[...seen, path].join(' → ')}`);
  seen.add(path);
  const node = path.split('.').reduce((n, key) => (n == null ? undefined : n[key]), tokens);
  if (!node || node.$value === undefined) throw new Error(`token tapılmadı: ${path}`);
  const ref = /^\{(.+)\}$/.exec(String(node.$value));
  return ref ? lookup(tokens, ref[1], seen) : String(node.$value);
}

export function luminance(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) throw new Error(`rəng #RRGGBB deyil: ${hex}`);
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(m[1].slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function ratio(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

export function check(tokens, pairs) {
  const rows = pairs.map(({ fg, bg, level }) => {
    if (!(level in LEVELS)) throw new Error(`naməlum level: ${level}`);
    const value = ratio(lookup(tokens, fg), lookup(tokens, bg));
    return { fg, bg, level, ratio: value, ok: value >= LEVELS[level] };
  });
  const saffron = lookup(tokens, 'color.brand.saffron').toLowerCase();
  const plum = lookup(tokens, 'color.brand.plum').toLowerCase();
  for (const { fg, bg } of pairs) {
    if (lookup(tokens, bg).toLowerCase() === saffron && lookup(tokens, fg).toLowerCase() !== plum) {
      rows.push({ fg, bg, level: 'brand', ratio: NaN, ok: false, note: 'saffron üzərində mətn plum olmalıdır' });
    }
  }
  return rows;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const tokens = JSON.parse(readFileSync(resolve(here, 'tokens.json'), 'utf8'));
  const { pairs } = JSON.parse(readFileSync(resolve(here, 'contrast-pairs.json'), 'utf8'));
  const rows = check(tokens, pairs);
  for (const r of rows) {
    const shown = Number.isNaN(r.ratio) ? '—' : `${r.ratio.toFixed(2)}:1`;
    console.log(`${r.ok ? '✅' : '❌'} ${r.fg} / ${r.bg}  ${shown}  (${r.level}${r.note ? `: ${r.note}` : ''})`);
  }
  if (rows.some((r) => !r.ok)) process.exit(1);
}
