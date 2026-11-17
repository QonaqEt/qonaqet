// tokens.json → CSS dəyişənləri (web) + Dart sinfi (mobile). Çıxışlar generasiya olunur və git-ə düşmür
// (.gitignore); CI və lokal build hər dəfə yenidən yaradır.
import StyleDictionary from 'style-dictionary';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

// Şrift adı string literalı olmalıdır: CSS-də `'Manrope'`, Dart-da `'Manrope'` (dırnaqsız Dart kodu kompilyasiya olunmur).
StyleDictionary.registerTransform({
  name: 'qn/fontFamily/quote',
  type: 'value',
  filter: (token) => (token.$type ?? token.type) === 'fontFamily',
  transform: (token) => `'${String(token.$value ?? token.value).replace(/'/g, "\\'")}'`,
});
const withQuotes = (group) => [...StyleDictionary.hooks.transformGroups[group], 'qn/fontFamily/quote'];
const out = (p) => resolve(here, p) + '/';

export const config = {
  source: [resolve(here, 'tokens.json')],
  log: { warnings: 'error', verbosity: 'verbose' },
  platforms: {
    css: {
      transforms: withQuotes('css'),
      prefix: 'qn',
      buildPath: out('../web/projects/design-tokens/src/lib/generated'),
      files: [{ destination: 'tokens.css', format: 'css/variables', options: { outputReferences: true } }],
    },
    flutter: {
      transforms: withQuotes('flutter'),
      buildPath: out('../mobile/lib/app/theme/generated'),
      files: [{ destination: 'qonaqet_tokens.dart', format: 'flutter/class.dart', options: { className: 'QonaqetTokens' } }],
    },
  },
};

if (import.meta.url === `file://${process.argv[1]}`) {
  const sd = new StyleDictionary(config);
  await sd.buildAllPlatforms();
}
