import 'dart:io';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/app/config/app_config.dart';
import 'package:qonaqet/app/di.dart';
import 'package:qonaqet/core/di/service_locator.dart';
import 'package:qonaqet/core/storage/secure_token_storage.dart';
import 'package:qonaqet/core/storage/token_storage.dart';
import 'package:secure_store/secure_store.dart';

/// Platform tərəfini təqlid edən yaddaş (Keychain/Keystore əvəzinə).
final _native = <String, String>{};
final _calls = <MethodCall>[];

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() {
    _native.clear();
    _calls.clear();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(
      SecureStore.defaultChannel,
      (call) async {
        _calls.add(call);
        final args = (call.arguments as Map<Object?, Object?>).cast<String, String>();
        return switch (call.method) {
          'read' => _native[args['key']],
          'write' => _native[args['key']!] = args['value']!,
          'delete' => _native.remove(args['key']),
          _ => throw MissingPluginException(),
        };
      },
    );
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SecureStore.defaultChannel, null);
  });

  group('SecureStore kanalı', () {
    test('oxu / yaz / sil', () async {
      const store = SecureStore();
      expect(await store.read('k'), isNull);
      await store.write('k', 'v');
      expect(await store.read('k'), 'v');
      await store.delete('k');
      expect(await store.read('k'), isNull);
      expect(_calls.map((c) => c.method), ['read', 'write', 'read', 'delete', 'read']);
    });

    test('platform xətası dəyər göstərmədən tipli xətaya çevrilir', () async {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(
        SecureStore.defaultChannel,
        (call) async => throw PlatformException(code: 'secure_store_failed', message: 'Keychain OSStatus -25300'),
      );
      await expectLater(
        const SecureStore().write('k', 'gizli-token'),
        throwsA(isA<SecureStoreException>()
            .having((e) => e.code, 'code', 'secure_store_failed')
            .having((e) => e.toString(), 'str', isNot(contains('gizli')))),
      );
    });
  });

  group('SecureTokenStorage', () {
    final tokens = SessionTokens(
      accessToken: 'acc',
      accessTokenExpiresAt: DateTime.utc(2026, 11, 28, 12, 15),
      refreshToken: 'ref',
      refreshTokenExpiresAt: DateTime.utc(2026, 12, 28),
    );

    test('token-lər tək açar altında saxlanılır, yeni nüsxə onları oxuyur (tətbiq yenidən açılması)', () async {
      await const SecureTokenStorage().write(tokens);
      expect(_native.keys, [SecureTokenStorage.key]);

      final restored = await const SecureTokenStorage().read();
      expect(restored!.accessToken, 'acc');
      expect(restored.refreshToken, 'ref');
      expect(restored.refreshTokenExpiresAt, tokens.refreshTokenExpiresAt);
    });

    test('clear silir', () async {
      await const SecureTokenStorage().write(tokens);
      await const SecureTokenStorage().clear();
      expect(_native, isEmpty);
      expect(await const SecureTokenStorage().read(), isNull);
    });

    test('zədələnmiş dəyər sessiya yoxdur sayılır və təmizlənir', () async {
      _native[SecureTokenStorage.key] = '{not json';
      expect(await const SecureTokenStorage().read(), isNull);
      expect(_native, isEmpty);
    });
  });

  test('DI token-ləri Keychain/Keystore-a yazır; lib/-də token shared_preferences-ə toxunmur', () {
    final sl = configureDependencies(AppConfig.dev(), locator: ServiceLocator());
    expect(sl.get<TokenStorage>(), isA<SecureTokenStorage>());

    final offenders = [
      for (final f in Directory('lib').listSync(recursive: true).whereType<File>())
        if (f.path.endsWith('.dart') &&
            f.readAsStringSync().contains('package:shared_preferences/') &&
            f.readAsStringSync().toLowerCase().contains('token'))
          f.path,
    ];
    expect(offenders, isEmpty);
  });
}
