using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Catalog;

/// <summary>Partnyor kataloqu sinxronladı (<c>PUT /partner/v1/catalog/{businessId}</c>) → webhook <c>catalog.synced</c>.</summary>
public sealed record CatalogSynced(BusinessId BusinessId, Guid ClientId, int Created, int Updated, int Unchanged, int Pruned, DateTimeOffset At) : DomainEvent(At);
