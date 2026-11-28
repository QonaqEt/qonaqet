import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../errors/api_exception.dart';
import '../storage/token_storage.dart';
import 'idempotency_key.dart';

/// Öz API klientimiz `http` üzərində (E14-T04; CLAUDE.md → Mobile → Networking).
///
/// - `Authorization: Bearer …` saxlanan sessiyadan; access token bitmək üzrədirsə sorğudan əvvəl yenilənir
/// - 401 gələrsə refresh bir dəfə edilir və sorğu **eyni** `Idempotency-Key` ilə təkrarlanır;
///   paralel 401-lər tək refresh-i paylaşır (backend refresh token-i rotasiya edir, ikinci istifadə = oğurluq)
/// - refresh alınmasa token-lər silinir, [onSessionExpired] çağırılır və [SessionExpiredException] atılır
/// - ProblemDetails → [ProblemException] (`code`); şəbəkə → [OfflineException] / [TimeoutApiException]
///
/// Token-lər, OTP-lər və hədiyyə linkləri heç vaxt loglanmır (bu klient ümumiyyətlə log yazmır).
final class ApiClient {
  ApiClient({
    required Uri baseUrl,
    required this._tokens,
    http.Client? httpClient,
    this.timeout = const Duration(seconds: 20),
    this.refreshSkew = const Duration(seconds: 30),
    this.onSessionExpired,
    DateTime Function()? clock,
  })  : _base = baseUrl,
        _http = httpClient ?? http.Client(),
        _now = clock ?? (() => DateTime.now().toUtc());

  static const apiPrefix = '/api/v1';
  static const refreshPath = 'auth/refresh';

  final Uri _base;
  final TokenStorage _tokens;
  final http.Client _http;
  final DateTime Function() _now;
  final Duration timeout;
  final Duration refreshSkew;

  /// Sessiya bərpa olunmayanda (login ekranına qayıtmaq üçün router bunu dinləyir).
  void Function(SessionExpiredException reason)? onSessionExpired;

  Future<SessionTokens?>? _refreshing;

  Future<Object?> get(String path, {Map<String, String>? query, bool auth = true}) =>
      send('GET', path, query: query, auth: auth);

  /// [idempotencyKey]: pul hərəkət etdirən POST-lar üçün məcburidir (CLAUDE.md → Conventions);
  /// [idempotent] `true` olsa açar avtomatik yaradılır.
  Future<Object?> post(
    String path, {
    Object? body,
    bool auth = true,
    bool idempotent = false,
    String? idempotencyKey,
  }) =>
      send(
        'POST',
        path,
        body: body,
        auth: auth,
        idempotencyKey: idempotencyKey ?? (idempotent ? newIdempotencyKey() : null),
      );

  Future<Object?> patch(String path, {Object? body}) => send('PATCH', path, body: body);

  Future<Object?> put(String path, {Object? body}) => send('PUT', path, body: body);

  Future<Object?> delete(String path, {Map<String, String>? query, Object? body}) =>
      send('DELETE', path, query: query, body: body);

  /// Ümumi çağırış. Uğurlu cavabın decode olunmuş JSON-u (boş gövdə → `null`).
  Future<Object?> send(
    String method,
    String path, {
    Map<String, String>? query,
    Object? body,
    bool auth = true,
    String? idempotencyKey,
  }) async {
    final uri = _uri(path, query);
    var session = auth ? await _validSession() : null;
    var response = await _send(method, uri, body, session, idempotencyKey);

    if (auth && response.statusCode == 401) {
      session = await _refresh(failedAccessToken: session?.accessToken);
      response = await _send(method, uri, body, session, idempotencyKey);
      if (response.statusCode == 401) {
        throw await _expire(const SessionExpiredException());
      }
    }
    return _decode(response);
  }

  /// Sessiya yoxdursa `null` (anonim sorğu); bitmək üzrədirsə əvvəlcədən yenilənir.
  Future<SessionTokens?> _validSession() async {
    final current = await _tokens.read();
    if (current == null || !current.accessExpiresWithin(refreshSkew, _now())) {
      return current;
    }
    return _refresh(failedAccessToken: current.accessToken);
  }

  /// Tək-uçuşlu refresh. [failedAccessToken] artıq dəyişibsə (başqa sorğu yeniləyib), yeni sessiya qaytarılır.
  Future<SessionTokens?> _refresh({String? failedAccessToken}) {
    return _refreshing ??= _doRefresh(failedAccessToken).whenComplete(() => _refreshing = null);
  }

  Future<SessionTokens?> _doRefresh(String? failedAccessToken) async {
    final current = await _tokens.read();
    if (current == null) {
      throw await _expire(const SessionExpiredException());
    }
    if (failedAccessToken != null &&
        current.accessToken != failedAccessToken &&
        !current.accessExpiresWithin(refreshSkew, _now())) {
      return current;
    }
    if (current.isRefreshExpired(_now())) {
      throw await _expire(const SessionExpiredException());
    }

    final response = await _send('POST', _uri(refreshPath, null), {'refreshToken': current.refreshToken}, null, null);
    if (response.statusCode >= 200 && response.statusCode < 300) {
      final json = _json(response);
      if (json is! Map<String, Object?>) {
        throw UnexpectedResponseException(response.statusCode);
      }
      final fresh = SessionTokens.fromJson(json);
      await _tokens.write(fresh);
      return fresh;
    }
    if (response.statusCode >= 500 || response.statusCode == 429) {
      // Server müvəqqəti əlçatmazdır: sessiyanı silmə, xətanı olduğu kimi ver.
      _decode(response);
    }
    final problem = _problem(response);
    throw await _expire(SessionExpiredException(blocked: problem?.code == 'user_blocked'));
  }

  Future<SessionExpiredException> _expire(SessionExpiredException reason) async {
    await _tokens.clear();
    onSessionExpired?.call(reason);
    return reason;
  }

  Future<http.Response> _send(
    String method,
    Uri uri,
    Object? body,
    SessionTokens? session,
    String? idempotencyKey,
  ) async {
    final request = http.Request(method, uri)
      ..headers['Accept'] = 'application/json'
      ..headers['Accept-Language'] = 'az';
    if (session != null) {
      request.headers['Authorization'] = 'Bearer ${session.accessToken}';
    }
    if (idempotencyKey != null) {
      request.headers['Idempotency-Key'] = idempotencyKey;
    }
    if (body != null) {
      request.headers['Content-Type'] = 'application/json; charset=utf-8';
      request.body = jsonEncode(body);
    }
    try {
      final streamed = await _http.send(request).timeout(timeout);
      return await http.Response.fromStream(streamed).timeout(timeout);
    } on TimeoutException {
      throw const TimeoutApiException();
    } on http.ClientException {
      throw const OfflineException();
    }
  }

  Object? _decode(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return _json(response);
    }
    throw _problem(response) ?? UnexpectedResponseException(response.statusCode);
  }

  ProblemException? _problem(http.Response response) {
    Object? json;
    try {
      json = _json(response);
    } on UnexpectedResponseException {
      json = null;
    }
    return ProblemException.fromJson(
      response.statusCode,
      json is Map<String, Object?> ? json : const {},
    );
  }

  Object? _json(http.Response response) {
    if (response.bodyBytes.isEmpty) {
      return null;
    }
    try {
      return jsonDecode(utf8.decode(response.bodyBytes));
    } on FormatException {
      throw UnexpectedResponseException(response.statusCode);
    }
  }

  Uri _uri(String path, Map<String, String>? query) {
    final clean = path.startsWith('/') ? path.substring(1) : path;
    final basePath = _base.path.endsWith('/') ? _base.path.substring(0, _base.path.length - 1) : _base.path;
    return _base.replace(
      path: '$basePath$apiPrefix/$clean',
      queryParameters: query == null || query.isEmpty ? null : query,
    );
  }

  void close() => _http.close();
}
