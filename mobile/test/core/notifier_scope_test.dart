import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/core/state/notifier_scope.dart';

void main() {
  testWidgets('of() dəyişikliyə görə yenidən qurur, read() qurmur', (tester) async {
    final counter = ValueNotifier<int>(0);
    var watchBuilds = 0;
    var readBuilds = 0;

    await tester.pumpWidget(
      NotifierScope<ValueNotifier<int>>(
        notifier: counter,
        child: Directionality(
          textDirection: TextDirection.ltr,
          child: Column(
            children: [
              Builder(
                builder: (context) {
                  watchBuilds++;
                  return Text('v${NotifierScope.of<ValueNotifier<int>>(context).value}');
                },
              ),
              Builder(
                builder: (context) {
                  readBuilds++;
                  NotifierScope.read<ValueNotifier<int>>(context);
                  return const SizedBox();
                },
              ),
            ],
          ),
        ),
      ),
    );

    counter.value = 1;
    await tester.pump();

    expect(find.text('v1'), findsOneWidget);
    expect(watchBuilds, 2);
    expect(readBuilds, 1);
  });

  testWidgets('scope yoxdursa aydın xəta', (tester) async {
    await tester.pumpWidget(
      Builder(
        builder: (context) {
          expect(() => NotifierScope.of<ValueNotifier<int>>(context), throwsFlutterError);
          return const SizedBox();
        },
      ),
    );
  });
}
