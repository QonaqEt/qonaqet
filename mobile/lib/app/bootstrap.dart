import 'package:flutter/widgets.dart';

import '../core/di/app_scope.dart';
import 'app.dart';
import 'config/app_config.dart';
import 'di.dart';

/// Bütün flavor-ların ortaq başlanğıcı.
Future<void> bootstrap(AppConfig config) async {
  WidgetsFlutterBinding.ensureInitialized();
  final locator = configureDependencies(config);
  runApp(AppScope(locator: locator, child: QonaqetApp(config: config)));
}
