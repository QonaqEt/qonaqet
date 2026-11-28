# secure_store

QONAQET-in öz Flutter plugin-i (E14-T05). Xarici paket yoxdur.

| Platforma | Saxlama |
|---|---|
| iOS | Keychain, `kSecClassGenericPassword`, `AfterFirstUnlockThisDeviceOnly` |
| Android | Keystore-da AES-256-GCM açarı; şifrəli mətn tətbiqin şəxsi SharedPreferences faylında |

```dart
const store = SecureStore();
await store.write('session', json);
final value = await store.read('session');
await store.delete('session');
```

Dəyərlər cihaz yenidən başlayandan sonra qalır. Android-də ehtiyat nüsxədən bərpa olunmuş, açarı olmayan dəyər
oxunanda silinir və `null` qaytarılır. Testlər tətbiqin `test/packages/secure_store_test.dart` faylındadır.
