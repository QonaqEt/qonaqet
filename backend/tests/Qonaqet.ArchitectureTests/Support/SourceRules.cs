using System.Text.RegularExpressions;

namespace Qonaqet.ArchitectureTests.Support;

/// <summary>Mənbə kodu qaydası: şərh silinmiş C# mətnində axtarılan nümunə.</summary>
internal sealed record SourceRule(string Name, Regex Pattern, Func<string, string, bool>? AppliesTo = null)
{
    /// <summary>Qaydanı bir fayla tətbiq edir; pozuntu sətirlərinin nömrələrini qaytarır.</summary>
    public IEnumerable<int> Violations(string relativePath, string source)
    {
        var code = SourceText.StripComments(source);
        if (AppliesTo is not null && !AppliesTo(relativePath, code))
        {
            yield break;
        }

        foreach (Match match in Pattern.Matches(code))
        {
            yield return code.AsSpan(0, match.Index).Count('\n') + 1;
        }
    }
}

internal static partial class SourceText
{
    /// <summary>
    /// <c>//</c> və <c>/* */</c> şərhlərini silir (sətir sayını saxlayaraq). Sətir literalları qalır —
    /// raw SQL məhz onların içindədir.
    /// </summary>
    public static string StripComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '"' || (c == '@' && next == '"') || (c == '$' && next == '"'))
            {
                var end = SkipString(source, i);
                result.Append(source, i, end - i);
                i = end;
            }
            else if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && next == '*')
            {
                i += 2;
                while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        result.Append('\n');
                    }

                    i++;
                }

                i += 2;
            }
            else
            {
                result.Append(c);
                i++;
            }
        }

        return result.ToString();
    }

    private static int SkipString(string s, int start)
    {
        var i = start;
        var verbatim = false;
        while (s[i] is '@' or '$')
        {
            verbatim |= s[i] == '@';
            i++;
        }

        // """raw""" literalları
        if (i + 2 < s.Length && s[i] == '"' && s[i + 1] == '"' && s[i + 2] == '"')
        {
            var close = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
            return close < 0 ? s.Length : close + 3;
        }

        i++;
        while (i < s.Length)
        {
            if (!verbatim && s[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (s[i] == '"')
            {
                if (verbatim && i + 1 < s.Length && s[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            if (!verbatim && s[i] == '\n')
            {
                return i;
            }

            i++;
        }

        return s.Length;
    }
}

/// <summary><c>CLAUDE.md</c> → "Soft delete" və "Coding rules" qadağaları.</summary>
internal static partial class ForbiddenSource
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static readonly SourceRule[] HardDeleteRules =
    [
        new("Mongo DeleteOne/DeleteMany", new Regex(@"\bDelete(One|Many)(Async)?\s*[(<]", Options)),
        new("Mongo FindOneAndDelete", new Regex(@"\bFindOneAndDelete(Async)?\s*[(<]", Options)),
        new("Mongo DropCollection/DropDatabase", new Regex(@"\bDrop(Collection|Database)(Async)?\s*\(", Options)),
        new("Mongo TTL index (ExpireAfter)", new Regex(@"\bExpireAfter\b", Options)),
        new("S3 DeleteObject", new Regex(@"\bDeleteObjects?(Async)?\s*\(|\bDeleteObjectRequest\b|\bDeleteObjectsRequest\b", Options)),
        new("EF ExecuteDelete", new Regex(@"\bExecuteDelete(Async)?\s*\(", Options)),
        new(
            "EF Remove/RemoveRange",
            new Regex(@"\.Remove(Range)?\s*\(", Options),
            (_, code) => code.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)),
        new("raw SQL DELETE/TRUNCATE", new Regex(@"\bDELETE\s+FROM\b|\bTRUNCATE\b", Options | RegexOptions.IgnoreCase)),
    ];

    /// <summary>
    /// Silinmiş qeydlər yalnız admin ssenarilərində görünür: <c>IncludeDeleted()</c> çağırışı yalnız
    /// <c>Features/Admin</c> və persistence qatında (repository-lərin özü) ola bilər.
    /// </summary>
    public static readonly SourceRule IncludeDeletedOutsideAdmin = new(
        "IncludeDeleted() admin xaricində",
        new Regex(@"\bIncludeDeleted\s*\(", Options),
        (path, _) => !path.Contains("/Features/Admin/", StringComparison.Ordinal) && !path.Contains("/Persistence/", StringComparison.Ordinal));

    /// <summary>Seed edilmiş kateqoriya kodları yalnız seed məlumatında görünə bilər — məntiq heç vaxt koda görə şaxələnmir.</summary>
    public static readonly SourceRule CategoryCodeLiteral = new(
        "kateqoriya kodu literalı",
        new Regex("\"(everyone|student|veteran-family|in-need)\"", Options),
        (path, _) => !path.Contains("/Seed/", StringComparison.Ordinal));
}
