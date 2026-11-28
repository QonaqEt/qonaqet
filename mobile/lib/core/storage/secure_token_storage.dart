import 'dart:convert';

import 'package:secure_store/secure_store.dart';

import 'token_storage.dart';

/// Sessiya token-ləri Keychain/Keystore-da (E14-T05). Bir açar altında JSON kimi saxlanılır ki, access və refresh
/// heç vaxt bir-birindən ayrı yazılmasın.
final class SecureTokenStorage implements TokenStorage {
  const SecureTokenStorage([this._store = const SecureStore()]);

  static const key = 'qonaqet.session';

  final SecureStore _store;

  @override
  Future<SessionTokens?> read() async {
    final raw = await _store.read(key);
    if (raw == null || raw.isEmpty) {
      return null;
    }
    try {
      return SessionTokens.fromJson(jsonDecode(raw) as Map<String, Object?>);
    } on Object {
      // Zədələnmiş/köhnə format: sessiya yoxdur kimi qəbul et və təmizlə.
      await _store.delete(key);
      return null;
    }
  }

  @override
  Future<void> write(SessionTokens tokens) => _store.write(key, jsonEncode(tokens.toJson()));

  @override
  Future<void> clear() => _store.delete(key);
}
