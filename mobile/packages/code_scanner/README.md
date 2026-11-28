# code_scanner

QONAQET-in öz QR skaner plugin-i (E14-T06). Dart tərəfində xarici paket yoxdur.

| Platforma | Skaner | Kamera icazəsi |
|---|---|---|
| Android | Google Code Scanner (`play-services-code-scanner`, ML Kit) — Play services-in öz ekranı | lazım deyil |
| iOS 16+ | VisionKit `DataScannerViewController` | `NSCameraUsageDescription` (tətbiqin Info.plist-i) |

```dart
switch (await const CodeScanner().scan()) {
  case ScanSuccess(:final value): ...
  case ScanCancelled(): ...
  case ScanPermissionDenied(): ...   // aydın mesaj + əl ilə kod girişi
  case ScanUnavailable(): ...        // əl ilə kod girişi
}
```

Tətbiqdə hazır UI: `lib/core/widgets/code_entry.dart` (`CodeEntry`) — skan düyməsi, icazə/əlçatanlıq mesajları və
qısa kodun əl ilə daxil edilməsi. Testlər tətbiqin `test/packages/code_scanner_test.dart` faylındadır.
