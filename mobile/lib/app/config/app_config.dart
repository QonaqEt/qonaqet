/// Tətbiqin qurulduğu mühit (E14-T01). Hər flavor-un öz giriş nöqtəsi var: `lib/main_<flavor>.dart`.
enum Flavor { dev, staging, prod }

/// Flavor-a bağlı konfiqurasiya. Sirr yoxdur — yalnız ictimai URL-lər.
///
/// URL-lər `--dart-define` ilə dəyişdirilə bilər (məs. fiziki cihazda lokal backend):
/// `flutter run --flavor dev -t lib/main_dev.dart --dart-define=QONAQET_API_BASE_URL=http://192.168.1.10:5080`
final class AppConfig {
  const AppConfig({
    required this.flavor,
    required this.appName,
    required this.apiBaseUrl,
    required this.publicBaseUrl,
  });

  /// Dev: Android emulator-dan host maşının `deploy/docker-compose.yml` API portu (5080).
  factory AppConfig.dev() => AppConfig(
        flavor: Flavor.dev,
        appName: 'QONAQET Dev',
        apiBaseUrl: _override(_apiOverride, 'http://10.0.2.2:5080'),
        publicBaseUrl: _override(_publicOverride, 'http://10.0.2.2:4200'),
      );

  factory AppConfig.staging() => AppConfig(
        flavor: Flavor.staging,
        appName: 'QONAQET Test',
        apiBaseUrl: _override(_apiOverride, 'https://api.staging.qonaqet.az'),
        publicBaseUrl: _override(_publicOverride, 'https://staging.qonaqet.az'),
      );

  factory AppConfig.prod() => AppConfig(
        flavor: Flavor.prod,
        appName: 'QONAQET',
        apiBaseUrl: _override(_apiOverride, 'https://api.qonaqet.az'),
        publicBaseUrl: _override(_publicOverride, 'https://qonaqet.az'),
      );

  final Flavor flavor;
  final String appName;

  /// Backend kökü, `/api/v1` prefiksi olmadan.
  final Uri apiBaseUrl;

  /// `App:PublicBaseUrl` — hədiyyə linkləri `{publicBaseUrl}/g/:token`.
  final Uri publicBaseUrl;

  bool get isProduction => flavor == Flavor.prod;

  static const _apiOverride = String.fromEnvironment('QONAQET_API_BASE_URL');
  static const _publicOverride = String.fromEnvironment('QONAQET_PUBLIC_BASE_URL');

  static Uri _override(String value, String fallback) =>
      Uri.parse(value.isEmpty ? fallback : value);
}
