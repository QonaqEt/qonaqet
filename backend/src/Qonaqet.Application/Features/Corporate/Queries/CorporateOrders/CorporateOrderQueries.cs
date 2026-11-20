using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Corporate.Queries.CorporateOrders;

/// <summary><c>GET corporate/me</c>: cari istifadəçinin şirkəti (portalın başlığı, brendinq).</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetMyCorporateQuery() : IQuery<CorporateDto>;

/// <summary><c>GET corporate/orders</c>: şirkətin sifarişləri, ən yenilər əvvəl.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record ListCorporateOrdersQuery() : IQuery<IReadOnlyList<CorporateOrderDto>>;

/// <summary><c>GET corporate/orders/{id}</c>.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetCorporateOrderQuery(CorporateGiftOrderId OrderId) : IQuery<CorporateOrderDto>;

public sealed class GetMyCorporateHandler(CorporateAccess access) : IQueryHandler<GetMyCorporateQuery, CorporateDto>
{
    public async Task<Result<CorporateDto>> Handle(GetMyCorporateQuery request, CancellationToken cancellationToken)
    {
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        return corporate.IsFailure ? corporate.Error : corporate.Value.ToDto();
    }
}

public sealed class ListCorporateOrdersHandler(CorporateAccess access, ICorporateGiftOrderRepository orders)
    : IQueryHandler<ListCorporateOrdersQuery, IReadOnlyList<CorporateOrderDto>>
{
    public async Task<Result<IReadOnlyList<CorporateOrderDto>>> Handle(ListCorporateOrdersQuery request, CancellationToken cancellationToken)
    {
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        return (await orders.ListForCorporateAsync(corporate.Value.Id, 200, cancellationToken).ConfigureAwait(false)).Select(o => o.ToDto()).ToList();
    }
}

public sealed class GetCorporateOrderHandler(CorporateAccess access) : IQueryHandler<GetCorporateOrderQuery, CorporateOrderDto>
{
    public async Task<Result<CorporateOrderDto>> Handle(GetCorporateOrderQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await access.RequireOrderAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        return found.IsFailure ? found.Error : found.Value.Order.ToDto();
    }
}
