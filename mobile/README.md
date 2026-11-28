# QONAQET mobil tətbiqi (Flutter)

Bir tətbiq, iki rejim: istifadəçi və biznes. Arxitektura, paket siyasəti və ekranlar: [CLAUDE.md](../CLAUDE.md) → "Mobile app".

## Quraşdırma

```bash
scripts/ci/install-flutter.sh            # pin olunmuş Flutter SDK (mobile/flutter-version)
(cd design-tokens && npm ci && npm run build)   # Dart theme generasiyası
cd mobile && flutter pub get --enforce-lockfile
```

## Flavor-lar

| Flavor | Giriş nöqtəsi | Android applicationId | iOS bundle id | API (default) |
|---|---|---|---|---|
| `dev` | `lib/main_dev.dart` | `az.qonaqet.app.dev` | `az.qonaqet.app.dev` | `http://10.0.2.2:5080` (emulator → lokal docker-compose) |
| `staging` | `lib/main_staging.dart` | `az.qonaqet.app.staging` | `az.qonaqet.app.staging` | `https://api.staging.qonaqet.az` |
| `prod` | `lib/main_prod.dart` | `az.qonaqet.app` | `az.qonaqet.app` | `https://api.qonaqet.az` |

```bash
flutter run --flavor dev -t lib/main_dev.dart
flutter build apk --debug --flavor dev -t lib/main_dev.dart
flutter build ios --flavor staging -t lib/main_staging.dart   # Xcode sxemləri: dev · staging · prod
```

URL-i dəyişmək (məs. fiziki cihaz): `--dart-define=QONAQET_API_BASE_URL=http://192.168.1.10:5080`,
`--dart-define=QONAQET_PUBLIC_BASE_URL=...`.

## Yoxlamalar (CI ilə eyni)

```bash
flutter analyze --fatal-infos
flutter test
```
