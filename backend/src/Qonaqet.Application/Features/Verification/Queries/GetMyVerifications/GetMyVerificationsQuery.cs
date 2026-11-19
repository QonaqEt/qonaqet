using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Verification.Queries.GetMyVerifications;

/// <summary>
/// <c>GET verification/me</c>: cari istifadəçinin təsdiqlənmiş kateqoriyaları (müddəti bitənlər <c>isValid: false</c>) və sorğularının
/// statusu/tarixçəsi (ən yenilər əvvəl).
/// </summary>
public sealed record GetMyVerificationsQuery : IQuery<MyVerificationsDto>;

public sealed record MyVerificationsDto(IReadOnlyList<VerifiedCategoryDto> Verified, IReadOnlyList<VerificationRequestDto> Requests);

public sealed class GetMyVerificationsHandler(IVerificationRequestRepository requests, ICategoryRepository categories, IUserRepository users, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetMyVerificationsQuery, MyVerificationsDto>
{
    public async Task<Result<MyVerificationsDto>> Handle(GetMyVerificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId!.Value;
        var items = await requests.ListForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        var verified = user?.VerifiedCategories ?? [];
        var lookup = new Dictionary<CategoryId, Category?>();
        foreach (var id in items.Select(i => i.CategoryId).Concat(verified.Select(v => CategoryId.From(v.CategoryId))).Distinct())
        {
            lookup[id] = await categories.GetAsync(id, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        return new MyVerificationsDto(
            [.. verified.Select(v =>
            {
                var category = lookup[CategoryId.From(v.CategoryId)];
                return new VerifiedCategoryDto(v.CategoryId, category?.Code, category?.Name, v.Subtype, v.ValidUntil, v.IsValidAt(now), v.VerifiedAt);
            })],
            [.. items.Select(i => i.ToDto(lookup[i.CategoryId]))]);
    }
}
