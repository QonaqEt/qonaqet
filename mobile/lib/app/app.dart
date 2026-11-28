import 'package:flutter/material.dart';

import 'config/app_config.dart';

/// Kök widget. Theme (E14-T03), routing (E14-T08) və DI (E14-T02) bura qoşulur.
class QonaqetApp extends StatelessWidget {
  const QonaqetApp({required this.config, super.key});

  final AppConfig config;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: config.appName,
      debugShowCheckedModeBanner: !config.isProduction,
      home: Scaffold(
        body: Center(child: Text(config.appName)),
      ),
    );
  }
}
