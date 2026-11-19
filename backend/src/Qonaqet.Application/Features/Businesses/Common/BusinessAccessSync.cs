using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Features.Businesses.Common;

/// <summary>İstifadəçinin biznes rollarını onun bizneslərə bağlılığına uyğunlaşdırır.</summary>
public sealed class BusinessAccessSync(IUserRepository users)
{
    /// <summary>Aktivləşdirilən biznesin müraciətçisini sahib edir.</summary>
    public async Task EnsureOwnerAsync(Business business, User owner, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.SyncBusinessRoles(true, owner.HasRole(Role.BusinessStaff), owner.BusinessId ?? business.Id.Value, at, null))
        {
            await users.UpdateAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }
}
