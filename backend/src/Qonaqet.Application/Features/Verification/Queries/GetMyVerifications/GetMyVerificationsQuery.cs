using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Verification.Queries.GetMyVerifications;

/// <summary><c>GET verification/me</c>: cari istifadəçinin təsdiq sorğularının statusu və tarixçəsi (ən yenilər əvvəl).</summary>
public sealed record GetMyVerificationsQuery : IQuery<MyVerificationsDto>;

public sealed record MyVerificationsDto(IReadOnlyList<VerificationRequestDto> Requests);

public sealed class GetMyVerificationsHandler(IVerificationRequestRepository requests, ICategoryRepository categories, ICurrentUser currentUser)
    : IQueryHandler<GetMyVerificationsQuery, MyVerificationsDto>
{
    public async Task<Result<MyVerificationsDto>> Handle(GetMyVerificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId!.Value;
        var items = await requests.ListForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var lookup = new Dictionary<CategoryId, Category?>();
        foreach (var id in items.Select(i => i.CategoryId).Distinct())
        {
            lookup[id] = await categories.GetAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return new MyVerificationsDto([.. items.Select(i => i.ToDto(lookup[i.CategoryId]))]);
    }
}
