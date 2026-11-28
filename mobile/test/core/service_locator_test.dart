import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/core/di/service_locator.dart';

class _Counter {
  _Counter() {
    created++;
  }
  static int created = 0;
}

void main() {
  late ServiceLocator sl;
  setUp(() {
    sl = ServiceLocator();
    _Counter.created = 0;
  });

  test('singleton eyni obyekti qaytarır', () {
    final c = _Counter();
    sl.registerSingleton(c);
    expect(sl.get<_Counter>(), same(c));
  });

  test('lazy singleton bir dəfə, ilk müraciətdə yaradılır', () {
    sl.registerLazySingleton(_Counter.new);
    expect(_Counter.created, 0);
    final a = sl.get<_Counter>();
    expect(sl.get<_Counter>(), same(a));
    expect(_Counter.created, 1);
  });

  test('factory hər dəfə yeni obyekt verir', () {
    sl.registerFactory(_Counter.new);
    expect(sl.get<_Counter>(), isNot(same(sl.get<_Counter>())));
  });

  test('qeydsiz tip və təkrar qeydiyyat xətadır; override və reset işləyir', () {
    expect(() => sl.get<_Counter>(), throwsStateError);
    sl.registerSingleton(_Counter());
    expect(() => sl.registerSingleton(_Counter()), throwsStateError);
    final fake = _Counter();
    sl.override(fake);
    expect(sl.get<_Counter>(), same(fake));
    sl.reset();
    expect(sl.isRegistered<_Counter>(), isFalse);
  });
}
