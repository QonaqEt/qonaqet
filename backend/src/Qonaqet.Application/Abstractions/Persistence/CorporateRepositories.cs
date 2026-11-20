using Qonaqet.Domain.Corporate;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>corporates</c>.</summary>
public interface ICorporateRepository : IRepository<Corporate, CorporateId>
{
    /// <summary>VÖEN silinməmiş başqa şirkətdə işlənirmi.</summary>
    Task<bool> VoenExistsAsync(string voen, CorporateId? except, CancellationToken cancellationToken);

    /// <summary>Şirkətlər ada görə.</summary>
    Task<IReadOnlyList<Corporate>> ListAsync(int limit, CancellationToken cancellationToken);
}
