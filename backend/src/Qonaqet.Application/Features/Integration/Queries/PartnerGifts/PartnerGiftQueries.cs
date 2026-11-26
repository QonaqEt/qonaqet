using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Commands.PartnerGifts;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Queries.PartnerGifts;

/// <summary><c>GET /partner/v1/gifts/{id}</c> (<c>gifts:read</c>): partnyorun öz hədiyyəsi — status, qalıq, müddət (link yox).</summary>
[RequiresRole(PartnerScopes.GiftsRead)]
public sealed record GetPartnerGiftQuery(GiftId GiftId) : IQuery<PartnerGiftDto>;

/// <summary><c>GET /partner/v1/balance</c>: ön ödənişli balans (qəpik).</summary>
[RequiresRole(PartnerScopes.ClientRole)]
public sealed record GetPartnerBalanceQuery : IQuery<PartnerBalanceDto>;

/// <summary><c>GET /partner/v1/ledger?page=&amp;pageSize=</c>: balans hərəkətləri, ən yenilər əvvəl.</summary>
[RequiresRole(PartnerScopes.ClientRole)]
public sealed record GetPartnerLedgerQuery(PageRequest Page) : IQuery<PagedResult<PartnerLedgerEntryDto>>;

public sealed record PartnerBalanceDto(long BalanceQepik);

public sealed record PartnerLedgerEntryDto(LedgerEntryType Type, long Amount, string RefType, Guid RefId, DateTimeOffset CreatedAt);

public sealed class GetPartnerGiftHandler(PartnerAccess access, IGiftRepository gifts, IClock clock) : IQueryHandler<GetPartnerGiftQuery, PartnerGiftDto>
{
    public async Task<Result<PartnerGiftDto>> Handle(GetPartnerGiftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        return gift is null || gift.PartnerClientId != client.Value.Id.Value ? GiftErrors.NotFound : gift.ToPartnerDto(clock.UtcNow);
    }
}

public sealed class GetPartnerBalanceHandler(PartnerAccess access, ILedgerRepository ledger) : IQueryHandler<GetPartnerBalanceQuery, PartnerBalanceDto>
{
    public async Task<Result<PartnerBalanceDto>> Handle(GetPartnerBalanceQuery request, CancellationToken cancellationToken)
    {
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        return client.IsFailure
            ? client.Error
            : new PartnerBalanceDto(await ledger.BalanceAsync(LedgerAccount.PartnerClient(client.Value.Id.Value), cancellationToken).ConfigureAwait(false));
    }
}

public sealed class GetPartnerLedgerHandler(PartnerAccess access, ILedgerRepository ledger)
    : IQueryHandler<GetPartnerLedgerQuery, PagedResult<PartnerLedgerEntryDto>>
{
    public async Task<Result<PagedResult<PartnerLedgerEntryDto>>> Handle(GetPartnerLedgerQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var page = await ledger.ListAsync(LedgerAccount.PartnerClient(client.Value.Id.Value), request.Page, cancellationToken).ConfigureAwait(false);
        return new PagedResult<PartnerLedgerEntryDto>(
            [.. page.Items.Select(e => new PartnerLedgerEntryDto(e.Type, e.Amount, e.RefType, e.RefId, e.CreatedAt))], page.Total);
    }
}
