import Flutter
import Foundation
import Security

/// iOS Keychain üzərində açar-dəyər saxlama (E14-T05).
///
/// `kSecClassGenericPassword`, xidmət adı = bundle id + ".secure_store". Əlçatanlıq
/// `AfterFirstUnlockThisDeviceOnly`: cihaz yenidən başlayıb bir dəfə açılandan sonra (fon push-ları da daxil)
/// oxunur, ehtiyat nüsxə ilə başqa cihaza keçmir. Dəyərlər heç vaxt loglanmır.
public class SecureStorePlugin: NSObject, FlutterPlugin {
  private let service = (Bundle.main.bundleIdentifier ?? "az.qonaqet.app") + ".secure_store"

  public static func register(with registrar: FlutterPluginRegistrar) {
    let channel = FlutterMethodChannel(name: "az.qonaqet/secure_store", binaryMessenger: registrar.messenger())
    registrar.addMethodCallDelegate(SecureStorePlugin(), channel: channel)
  }

  public func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
    guard let args = call.arguments as? [String: Any], let key = args["key"] as? String, !key.isEmpty else {
      result(FlutterError(code: "invalid_argument", message: "key tələb olunur", details: nil))
      return
    }
    switch call.method {
    case "read":
      read(key, result)
    case "write":
      write(key, args["value"] as? String ?? "", result)
    case "delete":
      let status = SecItemDelete(baseQuery(key) as CFDictionary)
      status == errSecSuccess || status == errSecItemNotFound ? result(nil) : fail(status, result)
    default:
      result(FlutterMethodNotImplemented)
    }
  }

  private func baseQuery(_ key: String) -> [String: Any] {
    [
      kSecClass as String: kSecClassGenericPassword,
      kSecAttrService as String: service,
      kSecAttrAccount as String: key,
    ]
  }

  private func read(_ key: String, _ result: FlutterResult) {
    var query = baseQuery(key)
    query[kSecReturnData as String] = true
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    var item: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &item)
    switch status {
    case errSecSuccess:
      guard let data = item as? Data, let value = String(data: data, encoding: .utf8) else {
        result(nil)
        return
      }
      result(value)
    case errSecItemNotFound:
      result(nil)
    default:
      fail(status, result)
    }
  }

  private func write(_ key: String, _ value: String, _ result: FlutterResult) {
    let data = Data(value.utf8)
    let attributes: [String: Any] = [
      kSecValueData as String: data,
      kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
    ]
    var status = SecItemUpdate(baseQuery(key) as CFDictionary, attributes as CFDictionary)
    if status == errSecItemNotFound {
      var add = baseQuery(key)
      add.merge(attributes) { _, new in new }
      status = SecItemAdd(add as CFDictionary, nil)
    }
    status == errSecSuccess ? result(nil) : fail(status, result)
  }

  private func fail(_ status: OSStatus, _ result: FlutterResult) {
    result(FlutterError(code: "secure_store_failed", message: "Keychain OSStatus \(status)", details: nil))
  }
}
