using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Queries.ListPartnerRedemptions;

/// <summary>
/// <c>GET /partner/v1/redemptions?businessId=&amp;from=&amp;to=&amp;page=&amp;pageSize=</c> (E13-T12): client-in biznesindəki təhvillər — hədiyyə
/// təhvilləri (geri alma yazıları da, mənfi dəyərlə) və təhvil olunmuş claim-lər, ən yenilər əvvəl. Default aralıq son 7 gün, ən çox 92 gün.
/// Kassa uzlaşması üçündür: kimin təhvil verdiyi (işçi) və alıcı/göndərən qaytarılmır, yalnız bu client-in özü olub-olmadığı.
/// </summary>
[RequiresRole(PartnerScopes.RedemptionsWrite)]
public sealed record ListPartnerRedemptionsQuery(Guid BusinessId, DateTimeOffset? From, DateTimeOffset? To, PageRequest Page)
    : IQuery<PagedResult<PartnerRedemptionItemDto>>;

/// <param name="Kind"><c>Gift</c> və ya <c>Claim</c>.</param>
/// <param name="ReversesRedemptionId">Geri alma yazısıdırsa, geri alınan təhvil.</param>
/// <param name="ByThisClient">Təhvili bu inteqrasiya client-i edib (kassa yox).</param>
public sealed record PartnerRedemptionItemDto(
    Guid RedemptionId, string Kind, Guid BusinessId, long ValueQepik, Guid? ReversesRedemptionId, bool ByThisClient, DateTimeOffset At);

public sealed class ListPartnerRedemptionsHandler(
    PartnerAccess access, IGiftRedemptionRepository giftRedemptions, IClaimRepository claims, IClock clock)
    : IQueryHandler<ListPartnerRedemptionsQuery, PagedResult<PartnerRedemptionItemDto>>
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(92);

    public async Task<Result<PagedResult<PartnerRedemptionItemDto>>> Handle(ListPartnerRedemptionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var to = request.To?.ToUniversalTime() ?? clock.UtcNow;
        var from = request.From?.ToUniversalTime() ?? to - DefaultRange;
        if (from >= to || to - from > MaxRange)
        {
            return IntegrationErrors.RangeInvalid;
        }

        var target = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (client, business) = target.Value;
        var clientId = client.Id.Value;

        // İki mənbənin birləşmiş səhifəsi: hər birindən (skip + pageSize) ən yenisi götürülür, birləşdirilib kəsilir.
        var window = request.Page.Skip + request.Page.PageSize;
        var gifts = await giftRedemptions.ListAtBusinessAsync(business.Id, from, to, window, cancellationToken).ConfigureAwait(false);
        var redeemedClaims = await claims.ListRedeemedAtBusinessAsync(business.Id, from, to, window, cancellationToken).ConfigureAwait(false);

        var items = gifts.Items
            .Select(r => new PartnerRedemptionItemDto(
                r.Id.Value, "Gift", business.Id.Value, r.ValueQepik, r.ReversesRedemptionId?.Value, r.StaffUserId == clientId, r.CreatedAt))
            .Concat(redeemedClaims.Items.Select(c => new PartnerRedemptionItemDto(
                c.Id.Value, "Claim", business.Id.Value, c.AmountQepik ?? 0, null, c.RedeemedByStaffId == clientId, c.RedeemedAt!.Value)))
            .OrderByDescending(i => i.At)
            .Skip(request.Page.Skip)
            .Take(request.Page.PageSize)
            .ToList();
        return new PagedResult<PartnerRedemptionItemDto>(items, gifts.Total + redeemedClaims.Total);
    }
}
