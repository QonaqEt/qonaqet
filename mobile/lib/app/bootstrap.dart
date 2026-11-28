import 'package:flutter/widgets.dart';

import 'app.dart';
import 'config/app_config.dart';

/// Bütün flavor-ların ortaq başlanğıcı.
Future<void> bootstrap(AppConfig config) async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(QonaqetApp(config: config));
}
