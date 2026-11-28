import 'package:code_scanner/code_scanner.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qonaqet/app/theme/app_theme.dart';
import 'package:qonaqet/core/utils/short_code.dart';
import 'package:qonaqet/core/widgets/code_entry.dart';

class _FakeScanner extends CodeScanner {
  _FakeScanner(this.outcome);
  final ScanOutcome outcome;
  @override
  Future<ScanOutcome> scan() async => outcome;
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('CodeScanner kanalı', () {
    final messenger = TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
    tearDown(() => messenger.setMockMethodCallHandler(CodeScanner.defaultChannel, null));

    Future<ScanOutcome> scanWith(Object? reply) {
      messenger.setMockMethodCallHandler(CodeScanner.defaultChannel, (call) async {
        expect(call.method, 'scan');
        if (reply is PlatformException) {
          throw reply;
        }
        return reply;
      });
      return const CodeScanner().scan();
    }

    test('status-lar tipli nəticəyə çevrilir', () async {
      expect(await scanWith({'status': 'success', 'value': 'Zy5hYmM'}), isA<ScanSuccess>().having((s) => s.value, 'v', 'Zy5hYmM'));
      expect(await scanWith({'status': 'cancelled'}), isA<ScanCancelled>());
      expect(await scanWith({'status': 'permission_denied'}), isA<ScanPermissionDenied>());
      expect(await scanWith({'status': 'unavailable', 'reason': 'ApiException'}),
          isA<ScanUnavailable>().having((s) => s.reason, 'r', 'ApiException'));
      expect(await scanWith(PlatformException(code: 'scan_in_progress')), isA<ScanUnavailable>());
    });

    test('plugin yoxdursa əlçatmaz sayılır', () async {
      expect(await const CodeScanner().scan(), isA<ScanUnavailable>());
    });
  });

  group('ShortCode', () {
    test('backend Normalize qaydası', () {
      expect(ShortCode.normalize('k7qm-2xrb'), 'K7QM2XRB');
      expect(ShortCode.normalize(' K7QM 2XRB '), 'K7QM2XRB');
      expect(ShortCode.normalize('OOOO-İıLl'), '00001111');
      expect(ShortCode.normalize('K7QM-2XR'), isNull, reason: 'qısa');
      expect(ShortCode.normalize('K7QM-2XRU'), isNull, reason: 'U əlifbada yoxdur');
      expect(ShortCode.normalize('K7QM-2XRə'), isNull);
      expect(ShortCode.normalize(''), isNull);
      expect(ShortCode.format('K7QM2XRB'), 'K7QM-2XRB');
    });
  });

  group('CodeEntry', () {
    Future<List<(String, CodeSource)>> pump(WidgetTester tester, ScanOutcome outcome) async {
      final codes = <(String, CodeSource)>[];
      await tester.pumpWidget(MaterialApp(
        theme: AppTheme.light(),
        home: Scaffold(
          body: CodeEntry(scanner: _FakeScanner(outcome), onCode: (c, s) => codes.add((c, s))),
        ),
      ));
      return codes;
    }

    testWidgets('uğurlu skan payload-u ötürür', (tester) async {
      final codes = await pump(tester, const ScanSuccess('Zy5wYXlsb2Fk'));
      await tester.tap(find.text('QR kodu skan et'));
      await tester.pumpAndSettle();
      expect(codes, [('Zy5wYXlsb2Fk', CodeSource.scan)]);
    });

    testWidgets('icazə rədd edilsə aydın mesaj və əl ilə giriş fokuslanır', (tester) async {
      final codes = await pump(tester, const ScanPermissionDenied());
      await tester.tap(find.text('QR kodu skan et'));
      await tester.pumpAndSettle();

      expect(find.text(CodeEntry.permissionDeniedMessage), findsOneWidget);
      final field = tester.widget<TextField>(find.byType(TextField));
      expect(field.focusNode!.hasFocus, isTrue);

      await tester.enterText(find.byType(TextField), 'k7qm-2xrb');
      await tester.tap(find.text('Təsdiqlə'));
      await tester.pump();
      expect(codes, [('K7QM2XRB', CodeSource.manual)]);
    });

    testWidgets('skaner əlçatmazdırsa mesaj; yanlış kod xəta göstərir', (tester) async {
      final codes = await pump(tester, const ScanUnavailable('missing_plugin'));
      await tester.tap(find.text('QR kodu skan et'));
      await tester.pumpAndSettle();
      expect(find.text(CodeEntry.unavailableMessage), findsOneWidget);

      await tester.enterText(find.byType(TextField), '123');
      await tester.tap(find.text('Təsdiqlə'));
      await tester.pump();
      expect(find.text(CodeEntry.invalidCodeMessage), findsOneWidget);
      expect(codes, isEmpty);
    });
  });
}
