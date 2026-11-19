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
    DateTimeOffset? ValidUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

/// <summary>Admin görünüşü: sahib, sənədlər (<c>GET files/{id}</c> ilə admin oxuyur), FIN verilibmi (hash-in özü yox).</summary>
public sealed record AdminVerificationDto(VerificationRequestDto Request, Guid UserId, string? UserEmail, IReadOnlyList<Guid> FileIds, bool HasFin);

/// <summary>İstifadəçinin cari təsdiqi; <see cref="IsValid"/> müddətin məntiqi yoxlamasıdır.</summary>
public sealed record VerifiedCategoryDto(
    Guid CategoryId, string? CategoryCode, string? CategoryName, string? Subtype, DateTimeOffset? ValidUntil, bool IsValid, DateTimeOffset VerifiedAt);

public static class VerificationMapping
{
    public static VerificationRequestDto ToDto(this VerificationRequest request, Category? category)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new VerificationRequestDto(
            request.Id, request.CategoryId, category?.Code, category?.Name, request.Subtype, request.Fields, request.FileIds.Count, request.Status,
            request.RejectReason, request.ValidUntil, request.CreatedAt, request.ReviewedAt);
    }

    public static AdminVerificationDto ToAdminDto(this VerificationRequest request, Category? category, Domain.Users.User? user)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AdminVerificationDto(request.ToDto(category), request.UserId, user?.Email, request.FileIds, request.FinHash is not null);
    }
}
