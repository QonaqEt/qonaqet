using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Donations.Queries.GetMyPoolDonations;

/// <summary>
/// <c>GET me/donations/pools</c>: ianəçinin hovuz və kampaniya ianələri — yalnız aqreqatlar (məbləğ, nə qədəri xərcləndi). Kimə ısmarlandığı heç
/// vaxt göstərilmir.
/// </summary>
public sealed record GetMyPoolDonationsQuery : IQuery<IReadOnlyList<PoolDonationSummaryDto>>;

public sealed record PoolDonationSummaryDto(
    Guid Id, Guid PoolId, Guid? CampaignId, long AmountQepik, long UsedQepik, PoolDonationStatus Status, DonorDisplay DonorDisplay, DateTimeOffset CreatedAt);

public sealed class GetMyPoolDonationsHandler(IPoolDonationRepository donations, ICurrentUser currentUser)
    : IQueryHandler<GetMyPoolDonationsQuery, IReadOnlyList<PoolDonationSummaryDto>>
{
    public const int Limit = 50;

    public async Task<Result<IReadOnlyList<PoolDonationSummaryDto>>> Handle(GetMyPoolDonationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var list = await donations.ListForDonorAsync(userId, Limit, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<PoolDonationSummaryDto>>.Success([.. list.Select(d => new PoolDonationSummaryDto(
            d.Id.Value, d.PoolId.Value, d.CampaignId, d.AmountQepik,
            d.Status == PoolDonationStatus.Confirmed ? d.AmountQepik - d.RemainingQepik : 0, d.Status, d.DonorDisplay, d.CreatedAt))]);
    }
}
