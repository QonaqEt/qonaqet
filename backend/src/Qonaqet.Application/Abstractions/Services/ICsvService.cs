namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Öz RFC 4180 CSV oxuyucu/yazıcımız (alıcı siyahıları, kupon kodları, hesabat ixracı).</summary>
public interface ICsvService
{
    /// <summary>Faylı oxuyur; xətalar exception deyil, sətir nömrəli hesabat kimi qayıdır.</summary>
    Task<CsvDocument> ReadAsync(Stream content, CsvReadOptions options, CancellationToken cancellationToken);

    /// <summary>Başlıq və sətirləri CRLF, UTF-8 (BOM ilə) yazır; formula injection-dan qorunur.</summary>
    Task WriteAsync(Stream destination, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken);
}

public sealed record CsvReadOptions
{
    public static readonly CsvReadOptions Default = new();

    public char Delimiter { get; init; } = ',';

    /// <summary>İlk sətir başlıqdır; bütün sətirlərdə sahə sayı başlıqla eyni olmalıdır.</summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>Ən çox data sətri (məs. korporativ alıcılar üçün 5 000). Aşanda xəta yazılır və oxuma dayanır.</summary>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>Bir xananın ən çox uzunluğu (simvol).</summary>
    public int MaxFieldLength { get; init; } = 10_000;

    /// <summary>Tamamilə boş sətirləri ötür.</summary>
    public bool SkipEmptyLines { get; init; } = true;
}

/// <summary>Bir data sətri; <see cref="LineNumber"/> sətrin fayldakı başladığı fiziki sətirdir (1-dən).</summary>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>Sətir nömrəli oxuma xətası; <see cref="Code"/> sabit koddur.</summary>
public sealed record CsvError(int LineNumber, string Code, string Message);

public sealed class CsvDocument(IReadOnlyList<string> header, IReadOnlyList<CsvRow> rows, IReadOnlyList<CsvError> errors)
{
    public IReadOnlyList<string> Header { get; } = header;

    public IReadOnlyList<CsvRow> Rows { get; } = rows;

    public IReadOnlyList<CsvError> Errors { get; } = errors;

    public bool IsValid => Errors.Count == 0;

    /// <summary>Başlıq adına görə xana (registrdən asılı deyil); sütun yoxdursa <c>null</c>.</summary>
    public string? Get(CsvRow row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);
        for (var i = 0; i < Header.Count; i++)
        {
            if (string.Equals(Header[i], column, StringComparison.OrdinalIgnoreCase))
            {
                return i < row.Fields.Count ? row.Fields[i] : null;
            }
        }

        return null;
    }
}
