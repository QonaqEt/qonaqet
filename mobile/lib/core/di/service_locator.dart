/// Öz kiçik service locator-umuz (E14-T02; CLAUDE.md → Mobile: "an own tiny service locator in core/di").
///
/// Qeydiyyat tipə görədir. `registerSingleton` hazır obyekti, `registerLazySingleton` ilk müraciətdə
/// bir dəfə yaradılanı, `registerFactory` hər müraciətdə yenisini verir. Testlərdə `reset()` ilə
/// təmizlənir və fake-lər qeyd olunur.
final class ServiceLocator {
  ServiceLocator();

  /// Tətbiq boyu bir nüsxə. Widget ağacında [AppScope] vasitəsilə də ötürülə bilər.
  static final ServiceLocator instance = ServiceLocator();

  final Map<Type, _Registration<Object>> _registrations = {};

  void registerSingleton<T extends Object>(T instance) =>
      _add<T>(_Registration<T>.value(instance));

  void registerLazySingleton<T extends Object>(T Function() create) =>
      _add<T>(_Registration<T>.lazy(create));

  void registerFactory<T extends Object>(T Function() create) =>
      _add<T>(_Registration<T>.factory(create));

  bool isRegistered<T extends Object>() => _registrations.containsKey(T);

  T get<T extends Object>() {
    final registration = _registrations[T];
    if (registration == null) {
      throw StateError('$T service locator-da qeyd olunmayıb');
    }
    return registration.resolve() as T;
  }

  /// Testlər üçün: mövcud qeydiyyatı əvəz etməyə icazə verir.
  void override<T extends Object>(T instance) =>
      _registrations[T] = _Registration<T>.value(instance);

  void reset() => _registrations.clear();

  void _add<T extends Object>(_Registration<T> registration) {
    if (_registrations.containsKey(T)) {
      throw StateError('$T artıq qeyd olunub');
    }
    _registrations[T] = registration;
  }
}

final class _Registration<T extends Object> {
  _Registration.value(T value)
      : _value = value,
        _create = null,
        _cache = true;
  _Registration.lazy(T Function() create)
      : _value = null,
        _create = create,
        _cache = true;
  _Registration.factory(T Function() create)
      : _value = null,
        _create = create,
        _cache = false;

  T? _value;
  final T Function()? _create;
  final bool _cache;

  T resolve() {
    final existing = _value;
    if (existing != null) {
      return existing;
    }
    final created = _create!();
    if (_cache) {
      _value = created;
    }
    return created;
  }
}
