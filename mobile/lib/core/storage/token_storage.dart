/// Sessiya token-ləri (backend `AuthTokensDto`). Yalnız Keychain/Keystore-da saxlanılır (E14-T05),
/// heç vaxt `shared_preferences`-də və loglarda yox.
final class SessionTokens {
  const SessionTokens({
    required this.accessToken,
    required this.accessTokenExpiresAt,
    required this.refreshToken,
    required this.refreshTokenExpiresAt,
  });

  factory SessionTokens.fromJson(Map<String, Object?> json) => SessionTokens(
        accessToken: json['accessToken']! as String,
        accessTokenExpiresAt: DateTime.parse(json['accessTokenExpiresAt']! as String).toUtc(),
        refreshToken: json['refreshToken']! as String,
        refreshTokenExpiresAt: DateTime.parse(json['refreshTokenExpiresAt']! as String).toUtc(),
      );

  final String accessToken;
  final DateTime accessTokenExpiresAt;
  final String refreshToken;
  final DateTime refreshTokenExpiresAt;

  Map<String, Object?> toJson() => {
        'accessToken': accessToken,
        'accessTokenExpiresAt': accessTokenExpiresAt.toIso8601String(),
        'refreshToken': refreshToken,
        'refreshTokenExpiresAt': refreshTokenExpiresAt.toIso8601String(),
      };

  /// Access token [skew] daxilində bitirsə, sorğudan əvvəl yenilənir.
  bool accessExpiresWithin(Duration skew, DateTime now) => !now.add(skew).isBefore(accessTokenExpiresAt);

  bool isRefreshExpired(DateTime now) => !now.isBefore(refreshTokenExpiresAt);

  /// Token-lər heç vaxt loga düşməsin.
  @override
  String toString() => 'SessionTokens(accessExpiresAt: $accessTokenExpiresAt)';
}

/// Token saxlama müqaviləsi. Real implementasiya `secure_store` plugin-i üzərindədir (E14-T05).
abstract interface class TokenStorage {
  Future<SessionTokens?> read();
  Future<void> write(SessionTokens tokens);
  Future<void> clear();
}

/// Testlər və ilkin quraşdırma üçün yaddaşda saxlama.
final class InMemoryTokenStorage implements TokenStorage {
  InMemoryTokenStorage([this._tokens]);

  SessionTokens? _tokens;

  @override
  Future<SessionTokens?> read() async => _tokens;

  @override
  Future<void> write(SessionTokens tokens) async => _tokens = tokens;

  @override
  Future<void> clear() async => _tokens = null;
}
