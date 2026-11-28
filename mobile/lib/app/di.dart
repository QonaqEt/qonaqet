import '../core/di/service_locator.dart';
import '../core/network/api_client.dart';
import '../core/storage/secure_token_storage.dart';
import '../core/storage/token_storage.dart';
import 'config/app_config.dart';

/// Tətbiq səviyyəsində asılılıqların qeydiyyatı. Hər feature öz `register<Feature>` funksiyasını bura əlavə edir
/// (məs. `registerAuth(locator)`), qeydiyyat ardıcıllığı: core → data → domain → presentation.
ServiceLocator configureDependencies(
  AppConfig config, {
  ServiceLocator? locator,
  TokenStorage? tokenStorage,
}) {
  final sl = locator ?? ServiceLocator.instance;
  sl
    ..registerSingleton<AppConfig>(config)
    // Token-lər yalnız Keychain/Keystore-da (E14-T05); testlər yaddaş implementasiyası verir.
    ..registerSingleton<TokenStorage>(tokenStorage ?? const SecureTokenStorage())
    ..registerLazySingleton<ApiClient>(
      () => ApiClient(baseUrl: config.apiBaseUrl, tokens: sl.get<TokenStorage>()),
    );
  return sl;
}
