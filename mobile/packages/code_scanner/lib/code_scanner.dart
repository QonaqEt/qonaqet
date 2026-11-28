import 'package:flutter/services.dart';

/// QR skanının nəticəsi.
sealed class ScanOutcome {
  const ScanOutcome();
}

/// Oxunan dəyər (QR payload-u, məs. `base64url("g." + giftId + "." + hmac)`).
final class ScanSuccess extends ScanOutcome {
  const ScanSuccess(this.value);
  final String value;
}

/// İstifadəçi skaneri bağladı.
final class ScanCancelled extends ScanOutcome {
  const ScanCancelled();
}

/// Kamera icazəsi rədd edilib (iOS). Android-də Google Code Scanner icazə istəmir.
final class ScanPermissionDenied extends ScanOutcome {
  const ScanPermissionDenied();
}

/// Skaner bu cihazda işləmir (Play services yoxdur, iOS 16-dan köhnə, kamera yoxdur).
final class ScanUnavailable extends ScanOutcome {
  const ScanUnavailable(this.reason);
  final String reason;
}

/// Öz QR skaner plugin-imiz (E14-T06): Android — Google Code Scanner (Play services, ML Kit),
/// iOS — VisionKit `DataScannerViewController`. Hər iki platformada skaner tam ekran açılır və tək nəticə qaytarır.
class CodeScanner {
  const CodeScanner([MethodChannel? channel]) : _channel = channel ?? defaultChannel;

  static const defaultChannel = MethodChannel('az.qonaqet/code_scanner');

  final MethodChannel _channel;

  Future<ScanOutcome> scan() async {
    try {
      final result = await _channel.invokeMapMethod<String, Object?>('scan');
      return switch (result?['status']) {
        'success' when result!['value'] is String => ScanSuccess(result['value']! as String),
        'cancelled' => const ScanCancelled(),
        'permission_denied' => const ScanPermissionDenied(),
        _ => ScanUnavailable(result?['reason'] as String? ?? 'unknown'),
      };
    } on MissingPluginException {
      return const ScanUnavailable('missing_plugin');
    } on PlatformException catch (e) {
      return ScanUnavailable(e.code);
    }
  }
}
