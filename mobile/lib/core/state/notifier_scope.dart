import 'package:flutter/widgets.dart';

/// `ChangeNotifier` / `ValueNotifier` əsaslı store-ları ağaca ötürən ümumi `InheritedNotifier`
/// (E14-T02; CLAUDE.md → Mobile: state yalnız Flutter built-in-ləri ilə, xarici kitabxana yoxdur).
///
/// ```dart
/// NotifierScope<SessionStore>(notifier: store, child: ...);
/// final store = NotifierScope.of<SessionStore>(context);      // dəyişəndə yenidən qurulur
/// final store = NotifierScope.read<SessionStore>(context);    // asılılıq yaratmır
/// ```
class NotifierScope<T extends Listenable> extends InheritedNotifier<T> {
  const NotifierScope({
    required T super.notifier,
    required super.child,
    super.key,
  });

  /// Store-u qaytarır və widget-i onun dəyişikliklərinə abunə edir.
  static T of<T extends Listenable>(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<NotifierScope<T>>();
    return _require(scope, context);
  }

  /// Store-u abunə olmadan qaytarır (callback-lərdə, `initState`-də).
  static T read<T extends Listenable>(BuildContext context) {
    final element = context.getElementForInheritedWidgetOfExactType<NotifierScope<T>>();
    return _require(element?.widget as NotifierScope<T>?, context);
  }

  static T _require<T extends Listenable>(NotifierScope<T>? scope, BuildContext context) {
    final notifier = scope?.notifier;
    if (notifier == null) {
      throw FlutterError('NotifierScope<$T> ağacda tapılmadı (${context.widget.runtimeType})');
    }
    return notifier;
  }
}
