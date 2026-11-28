import 'package:flutter/material.dart';

import 'config/app_config.dart';
import 'theme/app_theme.dart';

/// Kök widget. Routing (E14-T08) və DI (E14-T02) bura qoşulur.
class QonaqetApp extends StatelessWidget {
  const QonaqetApp({required this.config, super.key});

  final AppConfig config;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: config.appName,
      debugShowCheckedModeBanner: !config.isProduction,
      theme: AppTheme.light(),
      home: Scaffold(
        body: Center(child: Text(config.appName)),
      ),
    );
  }
}
