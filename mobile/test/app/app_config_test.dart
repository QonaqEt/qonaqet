import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/app/config/app_config.dart';

void main() {
  test('hər flavor öz API baza URL-i ilə qurulur', () {
    final configs = [AppConfig.dev(), AppConfig.staging(), AppConfig.prod()];

    expect(configs.map((c) => c.flavor), Flavor.values);
    expect(configs.map((c) => c.apiBaseUrl).toSet(), hasLength(3));
    expect(AppConfig.dev().apiBaseUrl.toString(), 'http://10.0.2.2:5080');
    expect(AppConfig.staging().apiBaseUrl.scheme, 'https');
    expect(AppConfig.prod().apiBaseUrl.toString(), 'https://api.qonaqet.az');
  });

  test('yalnız prod production sayılır', () {
    expect(AppConfig.prod().isProduction, isTrue);
    expect(AppConfig.dev().isProduction, isFalse);
    expect(AppConfig.staging().isProduction, isFalse);
  });
}
