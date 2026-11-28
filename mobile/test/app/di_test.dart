import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/app/config/app_config.dart';
import 'package:qonaqet/app/di.dart';
import 'package:qonaqet/core/di/service_locator.dart';
import 'package:qonaqet/core/network/api_client.dart';

void main() {
  test('core asılılıqları qeyd olunur', () {
    final sl = configureDependencies(AppConfig.dev(), locator: ServiceLocator());
    expect(sl.get<AppConfig>().flavor, Flavor.dev);
    expect(sl.get<ApiClient>(), same(sl.get<ApiClient>()));
  });
}
