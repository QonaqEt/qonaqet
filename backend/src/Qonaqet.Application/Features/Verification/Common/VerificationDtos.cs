using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Verification;

namespace Qonaqet.Application.Features.Verification.Common;

/// <summary>
/// Təsdiq sorğusu sahibinə və adminə necə görünür. FIN hash-i heç vaxt çıxmır. Kateqoriyanın adı burada göstərilir, çünki sorğunu
/// yalnız sahibi və admin görür; biznes/işçi tərəfində həssas kateqoriya <see cref="CategoryLabel"/> ilə neytral yazılır.
/// </summary>
public sealed record VerificationRequestDto(
    VerificationRequestId Id,
    CategoryId CategoryId,
    string? CategoryCode,
    string? CategoryName,
    string? Subtype,
    IReadOnlyDictionary<string, string> Fields,
    int FileCount,
    VerificationStatus Status,
    string? RejectReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

public static class VerificationMapping
{
    public static VerificationRequestDto ToDto(this VerificationRequest request, Category? category)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new VerificationRequestDto(
            request.Id, request.CategoryId, category?.Code, category?.Name, request.Subtype, request.Fields, request.FileIds.Count, request.Status,
            request.RejectReason, request.CreatedAt, request.ReviewedAt);
    }
}
