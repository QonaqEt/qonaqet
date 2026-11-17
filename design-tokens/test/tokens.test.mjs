// node --test test/ — token faylı, kontrast yoxlayıcısı və build çıxışları.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import StyleDictionary from 'style-dictionary';
import { check, lookup, ratio } from '../contrast.mjs';
import { config } from '../build.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const tokens = JSON.parse(readFileSync(join(root, 'tokens.json'), 'utf8'));

test('palette matches CLAUDE.md → Brand', () => {
  const expected = { nar: '#E8475F', saffron: '#FFB72B', mint: '#16B89B', plum: '#2A1A3E', cream: '#FFF8EF', blush: '#FFE3E6' };
  for (const [name, hex] of Object.entries(expected)) {
    assert.equal(lookup(tokens, `color.brand.${name}`).toUpperCase(), hex, name);
  }
  assert.equal(lookup(tokens, 'color.gradient.sunrise.start'), '#E8475F');
  assert.equal(lookup(tokens, 'color.gradient.sunrise.end'), '#FFB72B');
  assert.equal(lookup(tokens, 'color.gradient.together.start'), '#16B89B');
  assert.equal(lookup(tokens, 'color.gradient.together.end'), '#2A1A3E');
  assert.equal(lookup(tokens, 'font.family.ui'), 'Manrope');
  assert.equal(lookup(tokens, 'font.family.display'), 'Unbounded');
});

test('every gift theme has an accent colour', () => {
  for (const theme of ['birthday', 'love', 'thanks', 'family', 'congrats', 'justBecause', 'corporate']) {
    assert.match(lookup(tokens, `color.theme.${theme}`), /^#[0-9A-F]{6}$/i, theme);
  }
});

test('contrast ratio maths follows WCAG', () => {
  assert.equal(ratio('#FFFFFF', '#000000').toFixed(2), '21.00');
  assert.equal(ratio('#777777', '#777777'), 1);
  assert.equal(ratio('#000000', '#FFFFFF'), ratio('#FFFFFF', '#000000'));
});

test('committed contrast pairs all pass', () => {
  const { pairs } = JSON.parse(readFileSync(join(root, 'contrast-pairs.json'), 'utf8'));
  const failed = check(tokens, pairs).filter((r) => !r.ok);
  assert.deepEqual(failed, []);
});

test('failing pairs and non-plum text on saffron are reported', () => {
  const rows = check(tokens, [
    { fg: 'color.brand.cream', bg: 'color.brand.nar', level: 'AA' },
    { fg: 'color.brand.cream', bg: 'color.brand.saffron', level: 'AA-large' },
  ]);
  assert.equal(rows[0].ok, false);
  assert.ok(rows.some((r) => r.note === 'saffron üzərində mətn plum olmalıdır'));
  assert.throws(() => check(tokens, [{ fg: 'color.brand.nope', bg: 'color.brand.nar', level: 'AA' }]), /tapılmadı/);
});

test('build emits CSS variables and a compilable-looking Dart class', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'qn-tokens-'));
  const sd = new StyleDictionary({
    ...config,
    log: { warnings: 'error', verbosity: 'silent' },
    platforms: {
      css: { ...config.platforms.css, buildPath: join(dir, 'css') + '/' },
      flutter: { ...config.platforms.flutter, buildPath: join(dir, 'dart') + '/' },
    },
  });
  await sd.buildAllPlatforms();
  const css = readFileSync(join(dir, 'css', 'tokens.css'), 'utf8');
  assert.match(css, /--qn-color-brand-nar: #e8475f;/);
  assert.match(css, /--qn-color-text-on-saffron: var\(--qn-color-brand-plum\);/);
  assert.match(css, /--qn-font-family-ui: 'Manrope';/);
  const dart = readFileSync(join(dir, 'dart', 'qonaqet_tokens.dart'), 'utf8');
  assert.match(dart, /class QonaqetTokens \{/);
  assert.match(dart, /static const colorBrandNar = Color\(0xFFE8475F\);/);
  assert.match(dart, /static const fontFamilyUi = 'Manrope';/);
  assert.doesNotMatch(dart, /= [A-Za-z]+;/, 'dırnaqsız identifikator dəyəri qalmamalıdır');
  assert.deepEqual(readdirSync(join(dir, 'dart')), ['qonaqet_tokens.dart']);
});
