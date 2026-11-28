import '../core/di/service_locator.dart';
import 'config/app_config.dart';

/// Tətbiq səviyyəsində asılılıqların qeydiyyatı. Hər feature öz `register<Feature>` funksiyasını bura əlavə edir
/// (məs. `registerAuth(locator)`), qeydiyyat ardıcıllığı: core → data → domain → presentation.
ServiceLocator configureDependencies(AppConfig config, {ServiceLocator? locator}) {
  final sl = locator ?? ServiceLocator.instance;
  sl.registerSingleton<AppConfig>(config);
  return sl;
}
