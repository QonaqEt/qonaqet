import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:qonaqet/core/errors/api_exception.dart';
import 'package:qonaqet/core/network/api_client.dart';
import 'package:qonaqet/core/storage/token_storage.dart';

final _now = DateTime.utc(2026, 11, 28, 12);

SessionTokens _tokens(String access, {Duration accessLeft = const Duration(minutes: 10)}) => SessionTokens(
      accessToken: access,
      accessTokenExpiresAt: _now.add(accessLeft),
      refreshToken: 'r-$access',
      refreshTokenExpiresAt: _now.add(const Duration(days: 30)),
    );

http.Response _json(Object body, [int status = 200]) => http.Response(
      jsonEncode(body),
      status,
      headers: {'content-type': 'application/json; charset=utf-8'},
    );

Map<String, Object?> _refreshBody(String access) => {
      'accessToken': access,
      'accessTokenExpiresAt': _now.add(const Duration(minutes: 15)).toIso8601String(),
      'refreshToken': 'r-$access',
      'refreshTokenExpiresAt': _now.add(const Duration(days: 30)).toIso8601String(),
    };

void main() {
  late InMemoryTokenStorage storage;
  late List<http.Request> requests;
  final expired = <SessionExpiredException>[];

  ApiClient client(Future<http.Response> Function(http.Request) handler) => ApiClient(
        baseUrl: Uri.parse('https://api.test'),
        tokens: storage,
        httpClient: MockClient((r) {
          requests.add(r);
          return handler(r);
        }),
        clock: () => _now,
        onSessionExpired: expired.add,
      );

  setUp(() {
    storage = InMemoryTokenStorage(_tokens('a1'));
    requests = [];
    expired.clear();
  });

  test('auth header, /api/v1 prefiksi, query və JSON gövdə', () async {
    final api = client((r) async => _json({'ok': true}));

    final result = await api.get('me/claims', query: {'page': '2'});

    expect(result, {'ok': true});
    expect(requests.single.url.toString(), 'https://api.test/api/v1/me/claims?page=2');
    expect(requests.single.headers['Authorization'], 'Bearer a1');
    expect(requests.single.headers['Accept-Language'], 'az');
  });

  test('anonim sorğu Authorization göndərmir', () async {
    final api = client((r) async => http.Response('', 204));
    expect(await api.post('auth/otp/send', body: {'email': 'a@b.az'}, auth: false), isNull);
    expect(requests.single.headers.containsKey('Authorization'), isFalse);
    expect(jsonDecode(requests.single.body), {'email': 'a@b.az'});
  });

  test('pul hərəkəti: Idempotency-Key yaradılır və 401-dən sonrakı təkrarda eynidir', () async {
    final api = client((r) async {
      if (r.url.path.endsWith('auth/refresh')) {
        return _json(_refreshBody('a2'));
      }
      return r.headers['Authorization'] == 'Bearer a1' ? http.Response('', 401) : _json({'giftId': 'g'}, 201);
    });

    final result = await api.post('gifts', body: {'kind': 'Voucher'}, idempotent: true);

    expect(result, {'giftId': 'g'});
    final gifts = requests.where((r) => r.url.path.endsWith('/gifts')).toList();
    expect(gifts, hasLength(2));
    final key = gifts.first.headers['Idempotency-Key']!;
    expect(key, matches(RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')));
    expect(gifts.last.headers['Idempotency-Key'], key);
    expect(gifts.last.headers['Authorization'], 'Bearer a2');
    expect((await storage.read())!.accessToken, 'a2');
  });

  test('bitmək üzrə olan access token sorğudan əvvəl yenilənir', () async {
    storage = InMemoryTokenStorage(_tokens('a1', accessLeft: const Duration(seconds: 10)));
    final api = client((r) async => r.url.path.endsWith('auth/refresh') ? _json(_refreshBody('a2')) : _json({}));

    await api.get('me');

    expect(requests.map((r) => r.url.path), ['/api/v1/auth/refresh', '/api/v1/me']);
    expect(jsonDecode(requests.first.body), {'refreshToken': 'r-a1'});
    expect(requests.last.headers['Authorization'], 'Bearer a2');
  });

  test('paralel 401-lər tək refresh-i paylaşır', () async {
    final gate = Completer<void>();
    final api = client((r) async {
      if (r.url.path.endsWith('auth/refresh')) {
        await gate.future;
        return _json(_refreshBody('a2'));
      }
      return r.headers['Authorization'] == 'Bearer a1' ? http.Response('', 401) : _json({'p': r.url.path});
    });

    final futures = [api.get('a'), api.get('b'), api.get('c')];
    await Future<void>.delayed(Duration.zero);
    gate.complete();
    await Future.wait(futures);

    expect(requests.where((r) => r.url.path.endsWith('auth/refresh')), hasLength(1));
  });

  test('refresh rədd edilsə token-lər silinir və sessiya bitir', () async {
    final api = client((r) async => r.url.path.endsWith('auth/refresh')
        ? _json({'code': 'refresh_token_reused', 'title': 'x'}, 401)
        : http.Response('', 401));

    await expectLater(api.get('me'), throwsA(isA<SessionExpiredException>()));
    expect(await storage.read(), isNull);
    expect(expired, hasLength(1));
  });

  test('bloklanmış hesab ayrıca bildirilir', () async {
    final api = client((r) async => r.url.path.endsWith('auth/refresh')
        ? _json({'code': 'user_blocked', 'title': 'Hesab bloklanıb'}, 403)
        : http.Response('', 401));

    await expectLater(
      api.get('me'),
      throwsA(isA<SessionExpiredException>().having((e) => e.blocked, 'blocked', isTrue)),
    );
  });

  test('refresh zamanı server xətası sessiyanı silmir', () async {
    final api = client((r) async =>
        r.url.path.endsWith('auth/refresh') ? http.Response('', 503) : http.Response('', 401));

    await expectLater(api.get('me'), throwsA(isA<ProblemException>().having((e) => e.status, 'status', 503)));
    expect(await storage.read(), isNotNull);
    expect(expired, isEmpty);
  });

  test('ProblemDetails code-u və sahə xətaları tipli xətaya çevrilir', () async {
    final api = client((r) async => _json({
          'status': 422,
          'title': 'Hədiyyənin vaxtı keçib.',
          'code': 'gift_expired',
          'errors': [
            {'field': 'amountQepik', 'code': 'range', 'message': 'Məbləğ aralıqda deyil'},
          ],
        }, 422));

    final error = await api.get('gifts/1').then<ProblemException?>((_) => null, onError: (Object e) => e as ProblemException);

    expect(error!.code, 'gift_expired');
    expect(error.status, 422);
    expect(error.userMessage, 'Hədiyyənin vaxtı keçib.');
    expect(error.fieldErrors.single.field, 'amountQepik');
  });

  test('JSON olmayan xəta cavabı statusa görə kod və mesaj alır', () async {
    final api = client((r) async => http.Response('<html>', 502));
    await expectLater(
      api.get('x'),
      throwsA(isA<ProblemException>()
          .having((e) => e.code, 'code', 'server_error')
          .having((e) => e.userMessage, 'msg', contains('Serverdə problem'))),
    );
  });

  test('offline və timeout aydın mesajla', () async {
    final offline = client((r) async => throw http.ClientException('Failed host lookup'));
    await expectLater(
      offline.get('x'),
      throwsA(isA<OfflineException>().having((e) => e.userMessage, 'msg', contains('İnternet bağlantısı yoxdur'))),
    );

    final slow = ApiClient(
      baseUrl: Uri.parse('https://api.test'),
      tokens: storage,
      httpClient: MockClient((r) => Completer<http.Response>().future),
      timeout: const Duration(milliseconds: 10),
      clock: () => _now,
    );
    await expectLater(slow.get('x'), throwsA(isA<TimeoutApiException>()));
  });

  test('SessionTokens toString token sızdırmır', () {
    expect(_tokens('secret-access').toString(), isNot(contains('secret')));
  });
}
