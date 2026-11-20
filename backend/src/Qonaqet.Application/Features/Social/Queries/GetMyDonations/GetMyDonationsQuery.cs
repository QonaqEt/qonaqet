using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.GetMyDonations;

/// <summary>
/// <c>GET me/donations</c>: ianəçinin ısmarladıqları — yalnız aqreqatlar (neçə vahid, neçəsi verildi, neçəsi qalıb). Kimin aldığı, nə vaxt
/// və hansı kateqoriyadan olduğu heç vaxt göstərilmir. Hovuz və kampaniya ianələri E08-də bu siyahıya əlavə olunur.
/// </summary>
public sealed record GetMyDonationsQuery : IQuery<IReadOnlyList<DonationSummaryDto>>;

public sealed record DonationSummaryDto(
    OfferId OfferId, Guid BusinessId, string BusinessName, OfferType Type, string Title, int TotalQuantity, int Remaining, long Redeemed, DateTimeOffset CreatedAt);

public sealed class GetMyDonationsHandler(IOfferRepository offers, IClaimRepository claims, IBusinessRepository businesses, ICurrentUser currentUser)
    : IQueryHandler<GetMyDonationsQuery, IReadOnlyList<DonationSummaryDto>>
{
    public const int Limit = 50;

    public async Task<Result<IReadOnlyList<DonationSummaryDto>>> Handle(GetMyDonationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var donated = await offers.ListDonatedByAsync(userId, Limit, cancellationToken).ConfigureAwait(false);
        var result = new List<DonationSummaryDto>(donated.Count);
        foreach (var offer in donated)
        {
            var business = await businesses.GetAsync(offer.BusinessId, cancellationToken).ConfigureAwait(false);
            var redeemed = await claims.CountRedeemedForOfferAsync(offer.Id, cancellationToken).ConfigureAwait(false);
            result.Add(new DonationSummaryDto(
                offer.Id, offer.BusinessId.Value, business?.Name ?? string.Empty, offer.Type, offer.Title, offer.TotalQuantity, offer.Remaining, redeemed,
                offer.CreatedAt));
        }

        return Result<IReadOnlyList<DonationSummaryDto>>.Success(result);
    }
}
