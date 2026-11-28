import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Öz arxitektura testlərimiz (E14-T02): feature-first clean qaydaları mənbə skanı ilə yoxlanır.
///  - `domain/` Flutter-dən, `data/`-dan və `presentation/`-dan asılı deyil
///  - `data/` `presentation/`-dan asılı deyil
///  - bir feature digərinin `data/` və ya `presentation/` qatını import etmir (yalnız `domain/`)
///  - `core/` heç bir feature-dən asılı deyil
///  - xarici state/DI kitabxanası yoxdur
void main() {
  final files = Directory('lib')
      .listSync(recursive: true)
      .whereType<File>()
      .where((f) => f.path.endsWith('.dart') && !f.path.contains('/generated/'))
      .toList();

  Iterable<(String, String)> imports() sync* {
    final pattern = RegExp(r'''^\s*(?:import|export)\s+['"]([^'"]+)['"]''', multiLine: true);
    for (final file in files) {
      final path = file.path.replaceAll(r'\', '/');
      for (final m in pattern.allMatches(file.readAsStringSync())) {
        yield (path, _resolve(path, m.group(1)!));
      }
    }
  }

  test('domain qatı təmizdir', () {
    final violations = [
      for (final (file, target) in imports())
        if (file.contains('/domain/') &&
            (target.startsWith('package:flutter/') ||
                target.contains('/data/') ||
                target.contains('/presentation/')))
          '$file → $target',
    ];
    expect(violations, isEmpty);
  });

  test('data qatı presentation-dan asılı deyil', () {
    final violations = [
      for (final (file, target) in imports())
        if (file.contains('/data/') && target.contains('/presentation/')) '$file → $target',
    ];
    expect(violations, isEmpty);
  });

  test('feature-lər bir-birinin yalnız domain qatına müraciət edir', () {
    final violations = <String>[];
    for (final (file, target) in imports()) {
      final from = _feature(file);
      final to = _feature(target);
      if (from != null && to != null && from != to && !target.contains('/features/$to/domain/')) {
        violations.add('$file → $target');
      }
    }
    expect(violations, isEmpty);
  });

  test('core feature-lərdən asılı deyil', () {
    final violations = [
      for (final (file, target) in imports())
        if (file.startsWith('lib/core/') && target.contains('lib/features/')) '$file → $target',
    ];
    expect(violations, isEmpty);
  });

  test('xarici state/DI kitabxanası yoxdur', () {
    const forbidden = ['provider', 'riverpod', 'flutter_riverpod', 'bloc', 'flutter_bloc', 'get_it', 'getx', 'get', 'mobx'];
    final pubspec = File('pubspec.yaml').readAsStringSync();
    for (final name in forbidden) {
      expect(RegExp('^\\s+$name:', multiLine: true).hasMatch(pubspec), isFalse, reason: name);
    }
  });
}

String? _feature(String path) => RegExp(r'lib/features/([a-z_]+)/').firstMatch(path)?.group(1);

/// Nisbi və `package:qonaqet/` importlarını `lib/...` yoluna çevirir; digərləri olduğu kimi qalır.
String _resolve(String from, String target) {
  if (target.startsWith('package:qonaqet/')) {
    return 'lib/${target.substring('package:qonaqet/'.length)}';
  }
  if (target.contains(':')) {
    return target;
  }
  final parts = from.split('/')..removeLast();
  for (final segment in target.split('/')) {
    if (segment == '..') {
      parts.removeLast();
    } else if (segment != '.') {
      parts.add(segment);
    }
  }
  return parts.join('/');
}
