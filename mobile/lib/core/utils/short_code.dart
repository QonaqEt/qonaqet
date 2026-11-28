/// Qısa kodun (hədiyyə/claim) əl ilə daxil edilməsi — backend `RedemptionCodes.Normalize` ilə eyni qayda:
/// 8 simvol, Crockford-a bənzər əlifba (`0-9`, `I L O U` xaric hərflər), tire və boşluq nəzərə alınmır,
/// `O → 0`, `I/L → 1`; Azərbaycan klaviaturasındakı `İ`/`ı` da `1` sayılır.
abstract final class ShortCode {
  static const length = 8;
  static const _alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

  /// Normallaşdırılmış kod və ya kod deyilsə `null`.
  static String? normalize(String? input) {
    if (input == null || input.trim().isEmpty || input.length > 20) {
      return null;
    }
    final out = StringBuffer();
    for (final rune in input.trim().runes) {
      final c = switch (String.fromCharCode(rune)) {
        '-' || ' ' => null,
        'İ' || 'ı' || 'i' || 'I' || 'l' || 'L' => '1',
        'o' || 'O' => '0',
        final other => other.toUpperCase(),
      };
      if (c == null) {
        continue;
      }
      if (!_alphabet.contains(c) || c.length != 1) {
        return null;
      }
      out.write(c);
    }
    final code = out.toString();
    return code.length == length ? code : null;
  }

  /// Göstərmək üçün: `K7QM2XRB` → `K7QM-2XRB`.
  static String format(String code) =>
      code.length == length ? '${code.substring(0, 4)}-${code.substring(4)}' : code;
}
