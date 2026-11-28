/// API çağırışlarının tipli xətaları (E14-T04). UI yalnız [userMessage]-i göstərir;
/// məntiq [ProblemException.code]-a (backend-in sabit kodları, məs. `gift_expired`) baxır.
sealed class ApiException implements Exception {
  const ApiException();

  /// İstifadəçiyə göstərilən Azərbaycan dilində mesaj.
  String get userMessage;
}

/// Backend RFC 9457 ProblemDetails qaytardı (`code` genişlənməsi ilə).
final class ProblemException extends ApiException {
  const ProblemException({
    required this.status,
    required this.code,
    this.title,
    this.detail,
    this.fieldErrors = const [],
  });

  /// ProblemDetails gövdəsindən. `code` yoxdursa HTTP statusuna görə ümumi kod verilir.
  factory ProblemException.fromJson(int status, Map<String, Object?> json) {
    final errors = json['errors'];
    return ProblemException(
      status: status,
      code: json['code'] as String? ?? _fallbackCode(status),
      title: json['title'] as String?,
      detail: json['detail'] as String?,
      fieldErrors: [
        if (errors is List)
          for (final e in errors.whereType<Map<String, Object?>>())
            FieldError(
              field: e['field'] as String? ?? '',
              code: e['code'] as String? ?? '',
              message: e['message'] as String? ?? '',
            ),
      ],
    );
  }

  final int status;
  final String code;
  final String? title;
  final String? detail;
  final List<FieldError> fieldErrors;

  bool get isValidation => fieldErrors.isNotEmpty;

  /// Backend `title`-ı artıq Azərbaycan dilindədir (Error.Message); yoxdursa statusa görə ümumi mesaj.
  @override
  String get userMessage {
    final text = title;
    if (text != null && text.isNotEmpty) {
      return text;
    }
    return switch (status) {
      401 => 'Sessiyanın vaxtı bitib. Yenidən daxil olun.',
      403 => 'Bu əməliyyat üçün icazəniz yoxdur.',
      404 => 'Axtardığınız tapılmadı.',
      409 => 'Məlumat dəyişib. Yeniləyib təkrar cəhd edin.',
      429 => 'Çox sayda cəhd. Bir az gözləyib yenidən yoxlayın.',
      >= 500 => 'Serverdə problem yarandı. Bir az sonra yenidən cəhd edin.',
      _ => 'Sorğu yerinə yetirilmədi.',
    };
  }

  static String _fallbackCode(int status) => switch (status) {
        400 || 422 => 'validation_failed',
        401 => 'unauthorized',
        403 => 'forbidden',
        404 => 'not_found',
        409 => 'conflict',
        429 => 'too_many_requests',
        _ => 'server_error',
      };

  @override
  String toString() => 'ProblemException($status, $code)';
}

final class FieldError {
  const FieldError({required this.field, required this.code, required this.message});

  final String field;
  final String code;
  final String message;
}

/// Cihaz offline-dır və ya server əlçatmazdır (DNS, bağlantı rədd edildi).
final class OfflineException extends ApiException {
  const OfflineException();

  @override
  String get userMessage => 'İnternet bağlantısı yoxdur. Bağlantını yoxlayıb yenidən cəhd edin.';

  @override
  String toString() => 'OfflineException';
}

/// Server vaxtında cavab vermədi.
final class TimeoutApiException extends ApiException {
  const TimeoutApiException();

  @override
  String get userMessage => 'Server cavab vermir. Bir az sonra yenidən cəhd edin.';

  @override
  String toString() => 'TimeoutApiException';
}

/// Refresh alınmadı (token ləğv olunub, bitib və ya hesab bloklanıb) — istifadəçi yenidən daxil olmalıdır.
final class SessionExpiredException extends ApiException {
  const SessionExpiredException({this.blocked = false});

  final bool blocked;

  @override
  String get userMessage =>
      blocked ? 'Hesabınız bloklanıb.' : 'Sessiyanın vaxtı bitib. Yenidən daxil olun.';

  @override
  String toString() => 'SessionExpiredException(blocked: $blocked)';
}

/// Cavab gözlənilən formatda deyil (JSON deyil və s.).
final class UnexpectedResponseException extends ApiException {
  const UnexpectedResponseException(this.status);

  final int status;

  @override
  String get userMessage => 'Gözlənilməz cavab alındı. Bir az sonra yenidən cəhd edin.';

  @override
  String toString() => 'UnexpectedResponseException($status)';
}
