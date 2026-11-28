import 'package:code_scanner/code_scanner.dart';
import 'package:flutter/material.dart';

import '../utils/short_code.dart';

/// Kodun necə daxil edildiyi.
enum CodeSource { scan, manual }

/// QR skanı + əl ilə qısa kod girişi (E14-T06). Biznes rejimi (E16) və istifadəçi ekranları bunu işlədir.
///
/// Kamera icazəsi rədd ediləndə və ya skaner əlçatmaz olanda aydın mesaj göstərilir və əl ilə giriş sahəsi
/// fokuslanır. [onCode] skan olunmuş QR payload-u və ya normallaşdırılmış qısa kodu alır.
class CodeEntry extends StatefulWidget {
  const CodeEntry({
    required this.onCode,
    this.scanner = const CodeScanner(),
    super.key,
  });

  final void Function(String code, CodeSource source) onCode;
  final CodeScanner scanner;

  static const permissionDeniedMessage =
      'Kameraya icazə verilməyib. Parametrlərdə QONAQET üçün kameranı aktiv edin və ya kodu aşağıda əl ilə daxil edin.';
  static const unavailableMessage =
      'Bu cihazda QR skaneri işləmir. Kodu aşağıda əl ilə daxil edin.';
  static const invalidCodeMessage = 'Kod 8 simvoldan ibarət olmalıdır (məs. K7QM-2XRB).';

  @override
  State<CodeEntry> createState() => _CodeEntryState();
}

class _CodeEntryState extends State<CodeEntry> {
  final _controller = TextEditingController();
  final _focus = FocusNode();
  String? _notice;
  String? _error;
  bool _scanning = false;

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  Future<void> _scan() async {
    setState(() {
      _scanning = true;
      _notice = null;
    });
    final outcome = await widget.scanner.scan();
    if (!mounted) {
      return;
    }
    setState(() => _scanning = false);
    switch (outcome) {
      case ScanSuccess(:final value):
        widget.onCode(value, CodeSource.scan);
      case ScanCancelled():
        break;
      case ScanPermissionDenied():
        _showManual(CodeEntry.permissionDeniedMessage);
      case ScanUnavailable():
        _showManual(CodeEntry.unavailableMessage);
    }
  }

  void _showManual(String message) {
    setState(() => _notice = message);
    _focus.requestFocus();
  }

  void _submit() {
    final code = ShortCode.normalize(_controller.text);
    if (code == null) {
      setState(() => _error = CodeEntry.invalidCodeMessage);
      return;
    }
    setState(() => _error = null);
    widget.onCode(code, CodeSource.manual);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        FilledButton.icon(
          onPressed: _scanning ? null : _scan,
          icon: const Icon(Icons.qr_code_scanner),
          label: const Text('QR kodu skan et'),
        ),
        if (_notice case final notice?) ...[
          const SizedBox(height: 12),
          Semantics(
            liveRegion: true,
            child: Text(notice, style: theme.textTheme.bodyMedium),
          ),
        ],
        const SizedBox(height: 16),
        TextField(
          controller: _controller,
          focusNode: _focus,
          textCapitalization: TextCapitalization.characters,
          autocorrect: false,
          enableSuggestions: false,
          textInputAction: TextInputAction.done,
          onSubmitted: (_) => _submit(),
          decoration: InputDecoration(
            labelText: 'Və ya kodu daxil edin',
            hintText: 'K7QM-2XRB',
            errorText: _error,
          ),
        ),
        const SizedBox(height: 8),
        OutlinedButton(onPressed: _submit, child: const Text('Təsdiqlə')),
      ],
    );
  }
}
