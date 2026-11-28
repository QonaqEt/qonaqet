import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/app/theme/app_theme.dart';
import 'package:qonaqet/app/theme/generated/qonaqet_tokens.dart';

void main() {
  group('theme design token-lərdən', () {
    test('əsas rənglər və şriftlər token-dir', () {
      final theme = AppTheme.light();
      expect(theme.colorScheme.primary, QonaqetTokens.colorBrandNar);
      expect(theme.colorScheme.secondary, QonaqetTokens.colorBrandSaffron);
      expect(theme.colorScheme.onSecondary, QonaqetTokens.colorBrandPlum, reason: 'saffron üzərində mətn plum-dur');
      expect(theme.colorScheme.surface, QonaqetTokens.colorBrandCream);
      expect(theme.textTheme.displayLarge!.fontFamily, QonaqetTokens.fontFamilyDisplay);
      expect(theme.textTheme.bodyMedium!.fontFamily, QonaqetTokens.fontFamilyUi);
      expect(QonaqetColors.standard.giftThemes.keys, GiftThemeColor.values);
    });

    test('lib/-də hard-code rəng və şrift adı yoxdur', () {
      final rules = <RegExp, String>{
        RegExp(r'Color\(\s*0x'): 'Color(0x…)',
        RegExp(r'Color\.from(ARGB|RGBO)\('): 'Color.fromARGB/RGBO',
        RegExp(r'\bColors\.(?!transparent\b)[a-z]'): 'Material Colors.*',
        RegExp(r'''['"]#[0-9a-fA-F]{3,8}['"]'''): 'hex string',
        RegExp(r'''fontFamily:\s*['"]'''): 'şrift adı literalı',
      };
      final violations = <String>[];
      for (final file in Directory('lib').listSync(recursive: true).whereType<File>()) {
        final path = file.path.replaceAll(r'\', '/');
        if (!path.endsWith('.dart') || path.contains('/generated/')) {
          continue;
        }
        final lines = file.readAsLinesSync();
        for (var i = 0; i < lines.length; i++) {
          for (final MapEntry(key: rule, value: name) in rules.entries) {
            if (rule.hasMatch(lines[i])) {
              violations.add('$path:${i + 1} $name');
            }
          }
        }
      }
      expect(violations, isEmpty);
    });

    test('pubspec-dəki şrift ailələri token dəyərləri ilə eynidir', () {
      final pubspec = File('pubspec.yaml').readAsStringSync();
      for (final family in [QonaqetTokens.fontFamilyUi, QonaqetTokens.fontFamilyDisplay]) {
        expect(pubspec, contains('- family: $family'));
      }
    });
  });

  group('şriftlər Azərbaycan hərflərini dəstəkləyir', () {
    const letters = 'əƏğĞıİöÖüÜçÇşŞ';
    // Manrope-da (google/fonts, 2026-11) böyük Ə (U+018F) qlifi YOXDUR — kiçik ə var. Flutter bu tək qlifi
    // sistem şriftindən (Roboto / SF Pro) götürür, mətn düzgün oxunur, amma bu hərf başqa şriftlə görünür.
    // Qərar UI həmkarındadır (şrift dəyişikliyi və ya qəbul): docs/epics/E14.md → E14-T03 qeydi.
    // Manrope Ə-ni əlavə edəndə bu istisna silinməlidir — test onda qırılacaq və xatırladacaq.
    const knownMissing = {'Manrope-Variable.ttf': 'Ə'};
    for (final file in ['Manrope-Variable.ttf', 'Unbounded-Variable.ttf']) {
      test(file, () {
        final codepoints = _cmap(File('assets/fonts/$file').readAsBytesSync());
        final missing = letters.runes.where((r) => !codepoints.contains(r)).map(String.fromCharCode).join();
        expect(missing, knownMissing[file] ?? '');
      });
    }
  });

  testWidgets('Azərbaycan mətni şriftlə render olunur', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light(),
        home: const Scaffold(body: Text('Qonaq et, xoşbəxt et — Əli, Ğ, İ, ş, ç, ü, ö')),
      ),
    );
    expect(find.textContaining('xoşbəxt'), findsOneWidget);
  });
}

/// TrueType `cmap` cədvəlindən Unicode kod nöqtələri (format 4 və 12) — test üçün kiçik öz parser-imiz.
Set<int> _cmap(Uint8List bytes) {
  final data = ByteData.sublistView(bytes);
  final numTables = data.getUint16(4);
  int? cmap;
  for (var i = 0; i < numTables; i++) {
    final rec = 12 + i * 16;
    if (String.fromCharCodes(bytes.sublist(rec, rec + 4)) == 'cmap') {
      cmap = data.getUint32(rec + 8);
    }
  }
  if (cmap == null) {
    throw StateError('cmap cədvəli yoxdur');
  }
  final result = <int>{};
  final subtables = data.getUint16(cmap + 2);
  for (var i = 0; i < subtables; i++) {
    final rec = cmap + 4 + i * 8;
    final platform = data.getUint16(rec);
    final offset = cmap + data.getUint32(rec + 4);
    if (platform != 0 && platform != 3) {
      continue;
    }
    switch (data.getUint16(offset)) {
      case 4:
        final segX2 = data.getUint16(offset + 6);
        final ends = offset + 14;
        final starts = ends + segX2 + 2;
        for (var s = 0; s < segX2; s += 2) {
          final start = data.getUint16(starts + s);
          final end = data.getUint16(ends + s);
          if (start != 0xFFFF) {
            for (var c = start; c <= end; c++) {
              result.add(c);
            }
          }
        }
      case 12:
        final groups = data.getUint32(offset + 12);
        for (var g = 0; g < groups; g++) {
          final rec12 = offset + 16 + g * 12;
          for (var c = data.getUint32(rec12); c <= data.getUint32(rec12 + 4); c++) {
            result.add(c);
          }
        }
    }
  }
  return result;
}
