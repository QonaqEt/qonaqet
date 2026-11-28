import 'package:flutter/services.dart';

/// Həssas açar-dəyər saxlama: iOS Keychain, Android Keystore (AES-GCM, açar cihazdan çıxmır).
///
/// Sessiya token-ləri yalnız burada saxlanılır, `shared_preferences`-də yox (CLAUDE.md → Mobile → Storage).
/// Platform xətası [SecureStoreException] kimi gəlir; mesajda dəyər yoxdur.
class SecureStore {
  const SecureStore([MethodChannel? channel]) : _channel = channel ?? defaultChannel;

  static const defaultChannel = MethodChannel('az.qonaqet/secure_store');

  final MethodChannel _channel;

  Future<String?> read(String key) => _invoke<String>('read', {'key': key});

  Future<void> write(String key, String value) => _invoke<void>('write', {'key': key, 'value': value});

  Future<void> delete(String key) => _invoke<void>('delete', {'key': key});

  Future<T?> _invoke<T>(String method, Map<String, String> args) async {
    try {
      return await _channel.invokeMethod<T>(method, args);
    } on PlatformException catch (e) {
      throw SecureStoreException(e.code);
    }
  }
}

class SecureStoreException implements Exception {
  const SecureStoreException(this.code);

  final String code;

  @override
  String toString() => 'SecureStoreException($code)';
}
