import 'package:flutter/material.dart';

import 'generated/qonaqet_tokens.dart';

/// Rənglər və şriftlər yalnız design token-lərdən gəlir (E14-T03; CLAUDE.md → Brand).
/// `generated/qonaqet_tokens.dart` `design-tokens/`-dan Style Dictionary ilə yaradılır (`npm run build`).
///
/// Komponentlər rəngi `Theme.of(context).colorScheme` və ya [QonaqetColors] vasitəsilə alır; hex yazılmır
/// (`test/app/theme_test.dart` bunu yoxlayır).
abstract final class AppTheme {
  static ThemeData light() {
    const scheme = ColorScheme(
      brightness: Brightness.light,
      primary: QonaqetTokens.colorBrandNar,
      // nar üzərində cream yalnız iri/qalın mətndə AA-dır; düymə mətni qalındır (bax design-tokens/README).
      onPrimary: QonaqetTokens.colorTextOnNar,
      secondary: QonaqetTokens.colorBrandSaffron,
      onSecondary: QonaqetTokens.colorTextOnSaffron,
      tertiary: QonaqetTokens.colorBrandMint,
      onTertiary: QonaqetTokens.colorBrandPlum,
      error: QonaqetTokens.colorBrandNar,
      onError: QonaqetTokens.colorTextOnNar,
      surface: QonaqetTokens.colorBrandCream,
      onSurface: QonaqetTokens.colorTextDefault,
      surfaceContainerHighest: QonaqetTokens.colorBrandBlush,
      inverseSurface: QonaqetTokens.colorBrandPlum,
      onInverseSurface: QonaqetTokens.colorTextOnPlum,
    );

    final text = _textTheme(scheme.onSurface);
    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: scheme.surface,
      fontFamily: QonaqetTokens.fontFamilyUi,
      textTheme: text,
      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
        titleTextStyle: text.titleLarge,
      ),
      cardTheme: const CardThemeData(color: QonaqetTokens.colorBrandBlush, elevation: 0),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: scheme.primary,
          foregroundColor: scheme.onPrimary,
          textStyle: _ui(16, FontWeight.w700),
          minimumSize: const Size.fromHeight(52),
        ),
      ),
      extensions: const [QonaqetColors.standard],
    );
  }

  static TextTheme _textTheme(Color color) => TextTheme(
        displayLarge: _display(40, FontWeight.w700),
        displayMedium: _display(32, FontWeight.w700),
        displaySmall: _display(26, FontWeight.w600),
        headlineLarge: _display(24, FontWeight.w600),
        headlineMedium: _display(20, FontWeight.w600),
        headlineSmall: _display(18, FontWeight.w600),
        titleLarge: _ui(20, FontWeight.w700),
        titleMedium: _ui(16, FontWeight.w700),
        titleSmall: _ui(14, FontWeight.w700),
        bodyLarge: _ui(16, FontWeight.w500),
        bodyMedium: _ui(14, FontWeight.w500),
        bodySmall: _ui(12, FontWeight.w500),
        labelLarge: _ui(14, FontWeight.w700),
        labelMedium: _ui(12, FontWeight.w600),
        labelSmall: _ui(11, FontWeight.w600),
      ).apply(bodyColor: color, displayColor: color);

  static TextStyle _display(double size, FontWeight weight) =>
      _style(QonaqetTokens.fontFamilyDisplay, size, weight);

  static TextStyle _ui(double size, FontWeight weight) => _style(QonaqetTokens.fontFamilyUi, size, weight);

  /// Şriftlər variable-dır: çəki `wght` oxu ilə də verilir ki, bütün platformalarda düzgün göstərilsin.
  static TextStyle _style(String family, double size, FontWeight weight) => TextStyle(
        fontFamily: family,
        fontSize: size,
        fontWeight: weight,
        fontVariations: [FontVariation.weight(weight.value.toDouble())],
      );
}

/// ColorScheme-ə sığmayan brend rəngləri: gradientlər və hədiyyə temalarının vurğu rəngləri.
@immutable
class QonaqetColors extends ThemeExtension<QonaqetColors> {
  const QonaqetColors({
    required this.sunrise,
    required this.together,
    required this.giftThemes,
  });

  static const standard = QonaqetColors(
    sunrise: LinearGradient(
      colors: [QonaqetTokens.colorGradientSunriseStart, QonaqetTokens.colorGradientSunriseEnd],
    ),
    together: LinearGradient(
      colors: [QonaqetTokens.colorGradientTogetherStart, QonaqetTokens.colorGradientTogetherEnd],
    ),
    giftThemes: {
      GiftThemeColor.birthday: QonaqetTokens.colorThemeBirthday,
      GiftThemeColor.love: QonaqetTokens.colorThemeLove,
      GiftThemeColor.thanks: QonaqetTokens.colorThemeThanks,
      GiftThemeColor.family: QonaqetTokens.colorThemeFamily,
      GiftThemeColor.congrats: QonaqetTokens.colorThemeCongrats,
      GiftThemeColor.justBecause: QonaqetTokens.colorThemeJustBecause,
      GiftThemeColor.corporate: QonaqetTokens.colorThemeCorporate,
    },
  );

  /// Hero, hədiyyə kartları.
  final Gradient sunrise;

  /// Sosial/təsir bölmələri.
  final Gradient together;

  final Map<GiftThemeColor, Color> giftThemes;

  static QonaqetColors of(BuildContext context) => Theme.of(context).extension<QonaqetColors>() ?? standard;

  @override
  QonaqetColors copyWith({Gradient? sunrise, Gradient? together, Map<GiftThemeColor, Color>? giftThemes}) =>
      QonaqetColors(
        sunrise: sunrise ?? this.sunrise,
        together: together ?? this.together,
        giftThemes: giftThemes ?? this.giftThemes,
      );

  @override
  QonaqetColors lerp(QonaqetColors? other, double t) => t < 0.5 || other == null ? this : other;
}

/// Backend-in `GiftTheme` enum-u ilə eyni sıra (Birthday, Love, Thanks, Family, Congrats, JustBecause, Corporate).
enum GiftThemeColor { birthday, love, thanks, family, congrats, justBecause, corporate }
