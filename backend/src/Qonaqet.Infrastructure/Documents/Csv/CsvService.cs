using System.Text;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Documents.Csv;

/// <summary>
/// RFC 4180 oxuyucu/yazıcı. Dırnaqlı xanalar, <c>""</c> qaçışı, xana daxilində sətir keçidi, BOM,
/// CRLF / LF / CR sətir sonları. Mətn UTF-8 kimi oxunur, Azərbaycan hərfləri itmir.
/// </summary>
public sealed class CsvService : ICsvService
{
    public static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<CsvDocument> ReadAsync(Stream content, CsvReadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        using var reader = new StreamReader(content, StrictUtf8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string text;
        try
        {
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException)
        {
            return new CsvDocument([], [], [new CsvError(1, "invalid_encoding", "Fayl UTF-8 kodlaşmasında deyil.")]);
        }

        return Parse(text, options);
    }

    public static CsvDocument Parse(string text, CsvReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        return new Parser(text, options).Run();
    }

    public async Task WriteAsync(Stream destination, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        await using var writer = new StreamWriter(destination, Utf8WithBom, bufferSize: 16 * 1024, leaveOpen: true);
        await writer.WriteAsync(FormatLine(header).AsMemory(), cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(FormatLine(row).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bir sətri CRLF ilə formatlayır.</summary>
    public static string FormatLine(IReadOnlyList<string> fields, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(fields);
        var sb = new StringBuilder();
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(delimiter);
            }

            AppendField(sb, fields[i] ?? string.Empty, delimiter);
        }

        return sb.Append("\r\n").ToString();
    }

    private static void AppendField(StringBuilder sb, string value, char delimiter)
    {
        // Elektron cədvəllərdə formula injection-ın qarşısı: =, +, -, @, tab, CR ilə başlayan xana mətn kimi saxlanır.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.Length > 0 && (value[0] == ' ' || value[^1] == ' ')
            || value.AsSpan().IndexOfAny([delimiter, '"', '\r', '\n']) >= 0;

        if (!needsQuotes)
        {
            sb.Append(value);
            return;
        }

        sb.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    private sealed class Parser(string text, CsvReadOptions options)
    {
        private readonly List<CsvRow> _rows = [];
        private readonly List<CsvError> _errors = [];
        private int _pos = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        private int _line = 1;
        private string[] _header = [];

        public CsvDocument Run()
        {
            var isFirst = true;
            while (_pos < text.Length)
            {
                var startLine = _line;
                var fields = ReadRecord(out var recordError);

                if (recordError is not null)
                {
                    _errors.Add(new CsvError(startLine, recordError.Value.Code, recordError.Value.Message));
                    if (recordError.Value.Fatal)
                    {
                        break;
                    }

                    continue;
                }

                if (options.SkipEmptyLines && fields.Count == 1 && fields[0].Length == 0)
                {
                    continue;
                }

                if (isFirst && options.HasHeader)
                {
                    _header = fields.Select(f => f.Trim()).ToArray();
                    isFirst = false;
                    continue;
                }

                isFirst = false;
                if (options.HasHeader && fields.Count != _header.Length)
                {
                    _errors.Add(new CsvError(startLine, "field_count_mismatch", $"{_header.Length} sahə gözlənilirdi, {fields.Count} tapıldı."));
                    continue;
                }

                if (_rows.Count == options.MaxRows)
                {
                    _errors.Add(new CsvError(startLine, "too_many_rows", $"Ən çox {options.MaxRows} sətir ola bilər."));
                    break;
                }

                _rows.Add(new CsvRow(startLine, fields));
            }

            if (options.HasHeader && _header.Length == 0 && _errors.Count == 0)
            {
                _errors.Add(new CsvError(1, "missing_header", "Başlıq sətri yoxdur."));
            }

            return new CsvDocument(_header, _rows, _errors);
        }

        private List<string> ReadRecord(out (string Code, string Message, bool Fatal)? error)
        {
            error = null;
            var fields = new List<string>();
            var field = new StringBuilder();

            while (true)
            {
                if (_pos < text.Length && text[_pos] == '"' && field.Length == 0)
                {
                    _pos++;
                    if (!ReadQuoted(field, out error))
                    {
                        return fields;
                    }
                }
                else
                {
                    while (_pos < text.Length && text[_pos] != options.Delimiter && text[_pos] != '\r' && text[_pos] != '\n')
                    {
                        if (text[_pos] == '"')
                        {
                            error = ("unexpected_quote", "Dırnaqsız xananın içində dırnaq işarəsi var.", false);
                            SkipToEndOfLine();
                            return fields;
                        }

                        field.Append(text[_pos++]);
                    }
                }

                if (field.Length > options.MaxFieldLength)
                {
                    error = ("field_too_long", $"Xana {options.MaxFieldLength} simvoldan uzundur.", false);
                    SkipToEndOfLine();
                    return fields;
                }

                fields.Add(field.ToString());
                field.Clear();

                if (_pos >= text.Length)
                {
                    return fields;
                }

                var c = text[_pos];
                if (c == options.Delimiter)
                {
                    _pos++;
                    continue;
                }

                ConsumeNewLine();
                return fields;
            }
        }

        private bool ReadQuoted(StringBuilder field, out (string Code, string Message, bool Fatal)? error)
        {
            error = null;
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '"')
                {
                    if (_pos + 1 < text.Length && text[_pos + 1] == '"')
                    {
                        field.Append('"');
                        _pos += 2;
                        continue;
                    }

                    _pos++;
                    if (_pos < text.Length && text[_pos] != options.Delimiter && text[_pos] != '\r' && text[_pos] != '\n')
                    {
                        error = ("text_after_quote", "Bağlanan dırnaqdan sonra simvol var.", false);
                        SkipToEndOfLine();
                        return false;
                    }

                    return true;
                }

                if (c is '\r' or '\n')
                {
                    // Xana daxilində sətir keçidi saxlanır, fiziki sətir sayğacı artır.
                    if (c == '\r' && _pos + 1 < text.Length && text[_pos + 1] == '\n')
                    {
                        field.Append("\r\n");
                        _pos += 2;
                    }
                    else
                    {
                        field.Append(c);
                        _pos++;
                    }

                    _line++;
                    continue;
                }

                field.Append(c);
                _pos++;
            }

            error = ("unterminated_quote", "Dırnaq bağlanmayıb (faylın sonuna qədər).", true);
            return false;
        }

        private void SkipToEndOfLine()
        {
            while (_pos < text.Length && text[_pos] != '\r' && text[_pos] != '\n')
            {
                _pos++;
            }

            ConsumeNewLine();
        }

        private void ConsumeNewLine()
        {
            if (_pos >= text.Length)
            {
                return;
            }

            if (text[_pos] == '\r')
            {
                _pos++;
                if (_pos < text.Length && text[_pos] == '\n')
                {
                    _pos++;
                }
            }
            else if (text[_pos] == '\n')
            {
                _pos++;
            }

            _line++;
        }
    }
}
