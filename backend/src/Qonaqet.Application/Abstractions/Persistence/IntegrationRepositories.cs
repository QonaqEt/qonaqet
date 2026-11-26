using Qonaqet.Domain.Integration;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>integration_clients</c> (Postgres). Ləğv olunmuş (soft delete) client-lər qlobal filtrlə görünmür.</summary>
public interface IIntegrationClientRepository
{
    void Add(IntegrationClient client);

    Task<IntegrationClient?> GetAsync(IntegrationClientId id, CancellationToken cancellationToken);

    Task<IntegrationClient?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>Client-lər (sahibə görə süzülə bilər), ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<IntegrationClient>> ListAsync(IntegrationOwnerType? ownerType, Guid? ownerId, int limit, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
