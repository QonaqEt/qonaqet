using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.GetMyClaims;

/// <summary><c>GET me/claims</c>: alıcının son claim-ləri ("Aldıqlarım"); qüvvədə olan rezervin QR və qısa kodu ilə.</summary>
public sealed record GetMyClaimsQuery : IQuery<IReadOnlyList<ClaimDto>>;

/// <summary><c>GET me/claims/{id}</c>: bir claim (yalnız öz claim-i; başqasınınkı tapılmır).</summary>
public sealed record GetMyClaimQuery(ClaimId ClaimId) : IQuery<ClaimDto>;

public sealed class GetMyClaimsHandler(IClaimRepository claims, IBusinessRepository businesses, ClaimPresenter presenter, ICurrentUser currentUser)
    : IQueryHandler<GetMyClaimsQuery, IReadOnlyList<ClaimDto>>
{
    public const int Limit = 50;

    public async Task<Result<IReadOnlyList<ClaimDto>>> Handle(GetMyClaimsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var list = await claims.ListForRecipientAsync(userId, Limit, cancellationToken).ConfigureAwait(false);
        var names = new Dictionary<BusinessId, string>();
        var result = new List<ClaimDto>(list.Count);
        foreach (var claim in list)
        {
            if (!names.TryGetValue(claim.BusinessId, out var name))
            {
                name = await NameAsync(claim.BusinessId, cancellationToken).ConfigureAwait(false);
                names[claim.BusinessId] = name;
            }

            result.Add(presenter.Present(claim, name));
        }

        return Result<IReadOnlyList<ClaimDto>>.Success(result);
    }

    private async Task<string> NameAsync(BusinessId id, CancellationToken cancellationToken) =>
        (await businesses.GetAsync(id, cancellationToken).ConfigureAwait(false))?.Name ?? string.Empty;
}

public sealed class GetMyClaimHandler(IClaimRepository claims, IBusinessRepository businesses, ClaimPresenter presenter, ICurrentUser currentUser)
    : IQueryHandler<GetMyClaimQuery, ClaimDto>
{
    public async Task<Result<ClaimDto>> Handle(GetMyClaimQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var claim = await claims.GetAsync(request.ClaimId, cancellationToken).ConfigureAwait(false);
        if (claim is null || claim.RecipientUserId != currentUser.ActorId)
        {
            return SocialErrors.ClaimNotFound;
        }

        var business = await businesses.GetAsync(claim.BusinessId, cancellationToken).ConfigureAwait(false);
        return presenter.Present(claim, business?.Name ?? string.Empty);
    }
}
