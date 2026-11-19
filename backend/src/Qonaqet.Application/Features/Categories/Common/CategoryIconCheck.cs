using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Files;

namespace Qonaqet.Application.Features.Categories.Common;

/// <summary>Kateqoriya ikonu: mövcud, silinməmiş, loqo təyinatlı ictimai şəkil (ikonlar loqo siyasəti ilə yüklənir).</summary>
public sealed class CategoryIconCheck(IStoredFileRepository files)
{
    public async Task<bool> IsUsableAsync(Guid? fileId, Guid? currentValue, CancellationToken cancellationToken)
    {
        if (fileId is null || fileId == currentValue)
        {
            return true;
        }

        var file = await files.GetAsync(FileId.From(fileId.Value), cancellationToken).ConfigureAwait(false);
        return file is { Purpose: FilePurpose.Logo };
    }
}

/// <summary>Kateqoriya auditinin data hissəsi.</summary>
internal static class CategoryAudit
{
    public static Dictionary<string, string?> Data(Category category) => new()
    {
        ["code"] = category.Code,
        ["isActive"] = category.IsActive ? "true" : "false",
        ["isOpen"] = category.IsOpen ? "true" : "false",
        ["privacyLevel"] = category.PrivacyLevel.ToString(),
    };
}
