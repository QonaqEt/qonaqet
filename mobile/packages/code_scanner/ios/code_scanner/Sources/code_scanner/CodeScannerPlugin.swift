import AVFoundation
import Flutter
import UIKit
import VisionKit

/// QR skaneri: VisionKit `DataScannerViewController` (iOS 16+, A12+) — E14-T06.
///
/// Kamera icazəsi soruşulur; rədd edilibsə `permission_denied`, cihaz dəstəkləmirsə `unavailable` qaytarılır
/// və Dart tərəfi əl ilə kod girişini təklif edir. Tətbiqin Info.plist-ində `NSCameraUsageDescription` olmalıdır.
public class CodeScannerPlugin: NSObject, FlutterPlugin {
  private var pending: FlutterResult?
  private var scannerDelegate: AnyObject?

  public static func register(with registrar: FlutterPluginRegistrar) {
    let channel = FlutterMethodChannel(name: "az.qonaqet/code_scanner", binaryMessenger: registrar.messenger())
    registrar.addMethodCallDelegate(CodeScannerPlugin(), channel: channel)
  }

  public func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
    guard call.method == "scan" else {
      result(FlutterMethodNotImplemented)
      return
    }
    guard pending == nil else {
      result(FlutterError(code: "scan_in_progress", message: "Skan artıq açıqdır", details: nil))
      return
    }
    switch AVCaptureDevice.authorizationStatus(for: .video) {
    case .authorized:
      present(result)
    case .notDetermined:
      AVCaptureDevice.requestAccess(for: .video) { granted in
        DispatchQueue.main.async {
          granted ? self.present(result) : result(["status": "permission_denied"])
        }
      }
    default:
      result(["status": "permission_denied"])
    }
  }

  private func present(_ result: @escaping FlutterResult) {
    guard #available(iOS 16.0, *), DataScannerViewController.isSupported, DataScannerViewController.isAvailable,
      let root = Self.topViewController()
    else {
      result(["status": "unavailable", "reason": "data_scanner_unsupported"])
      return
    }
    pending = result
    let scanner = DataScannerViewController(
      recognizedDataTypes: [.barcode(symbologies: [.qr])],
      qualityLevel: .balanced,
      isHighlightingEnabled: true
    )
    let delegate = ScannerDelegate { [weak self] value in self?.finish(value) }
    scannerDelegate = delegate
    scanner.delegate = delegate
    scanner.navigationItem.leftBarButtonItem = UIBarButtonItem(
      systemItem: .cancel,
      primaryAction: UIAction { [weak self] _ in self?.finish(nil) }
    )
    let navigation = UINavigationController(rootViewController: scanner)
    navigation.modalPresentationStyle = .fullScreen
    navigation.presentationController?.delegate = delegate
    root.present(navigation, animated: true) {
      try? scanner.startScanning()
    }
  }

  private func finish(_ value: String?) {
    guard let result = pending else { return }
    pending = nil
    scannerDelegate = nil
    Self.topViewController()?.dismiss(animated: true)
    if let value, !value.isEmpty {
      result(["status": "success", "value": value])
    } else {
      result(["status": "cancelled"])
    }
  }

  private static func topViewController() -> UIViewController? {
    let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
    var top = scenes.flatMap(\.windows).first(where: \.isKeyWindow)?.rootViewController
    while let presented = top?.presentedViewController {
      top = presented
    }
    return top
  }
}

@available(iOS 16.0, *)
private final class ScannerDelegate: NSObject, DataScannerViewControllerDelegate,
  UIAdaptivePresentationControllerDelegate
{
  private let onResult: (String?) -> Void
  private var done = false

  init(onResult: @escaping (String?) -> Void) {
    self.onResult = onResult
  }

  func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
    for item in addedItems {
      if case .barcode(let barcode) = item, let value = barcode.payloadStringValue {
        complete(value)
        return
      }
    }
  }

  func dataScanner(_ dataScanner: DataScannerViewController, becameUnavailableWithError error: DataScannerViewController.ScanningUnavailable) {
    complete(nil)
  }

  func presentationControllerDidDismiss(_ presentationController: UIPresentationController) {
    complete(nil)
  }

  private func complete(_ value: String?) {
    guard !done else { return }
    done = true
    onResult(value)
  }
}
