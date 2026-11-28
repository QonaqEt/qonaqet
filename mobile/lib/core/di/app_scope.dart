import 'package:flutter/widgets.dart';

import 'service_locator.dart';

/// Service locator-u widget ağacına ötürür ki, səhifələr qlobal statikdən deyil, kontekstdən oxusun
/// (testlərdə fake locator vermək asan olur).
class AppScope extends InheritedWidget {
  const AppScope({required this.locator, required super.child, super.key});

  final ServiceLocator locator;

  static ServiceLocator of(BuildContext context) {
    final scope = context.getInheritedWidgetOfExactType<AppScope>();
    if (scope == null) {
      throw FlutterError('AppScope ağacda tapılmadı');
    }
    return scope.locator;
  }

  @override
  bool updateShouldNotify(AppScope oldWidget) => locator != oldWidget.locator;
}

extension ServiceLocatorContext on BuildContext {
  /// `context.get<GiftRepository>()`
  T get<T extends Object>() => AppScope.of(this).get<T>();
}
