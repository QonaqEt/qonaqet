using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Businesses.Common;

/// <summary>Biznes daxilində lazım olan icazə: <see cref="Manage"/> yalnız sahibə, <see cref="Operate"/> sahib və işçiyə.</summary>
public enum BusinessPermission
{
    /// <summary>Kataloq, təkliflər, işçilər, parametrlər, hesabatlar.</summary>
    Manage,

    /// <summary>Kod yoxlama/təhvil, bugünkü əməliyyatlar.</summary>
    Operate,
}

/// <summary>Uğurlu giriş yoxlamasının nəticəsi: biznes və aktorun orada rolu (admin üçün <c>null</c>).</summary>
public sealed record BusinessAccessGrant(Business Business, MembershipRole? Role)
{
    public bool IsAdmin => Role is null;

    /// <summary>Maliyyə sahələrini (VÖEN, IBAN) görə bilər: sahib və admin.</summary>
    public bool SeesFinancials => Role is null or MembershipRole.Owner;
}

/// <summary>
/// Handler-lərdə resurs yoxlaması: "bu aktor bu biznesdə bu işi görə bilərmi". Endpoint-lər biznesi həmişə açıq qəbul edir
/// (<c>/business/{businessId}/…</c>), səssiz "default biznes" yoxdur. Üzvlük Mongo-dan oxunur (JWT-dəki <c>business_id</c>
/// yox), ona görə şəbəkə sahibləri bütün bizneslərinə, yeni əlavə olunan işçi isə token yenilənmədən çata bilir.
/// Üzv olmayan həmişə <c>403 business_access_denied</c> alır — biznesin olub-olmaması ona açıqlanmır.
/// </summary>
public sealed class BusinessAccess(IBusinessRepository businesses, IBusinessMembershipRepository memberships, ICurrentUser currentUser)
{
    public async Task<Result<BusinessAccessGrant>> RequireAsync(BusinessId businessId, BusinessPermission permission, CancellationToken cancellationToken)
    {
        if (currentUser.ActorId is not { } actor)
        {
            return CommonErrors.Unauthorized;
        }

        if (currentUser.IsAdmin())
        {
            var any = await businesses.GetAsync(businessId, cancellationToken).ConfigureAwait(false);
            return any is null ? BusinessErrors.NotFound : new BusinessAccessGrant(any, null);
        }

        var membership = await memberships.FindAsync(businessId, actor, cancellationToken).ConfigureAwait(false);
        if (membership is null || (permission == BusinessPermission.Manage && membership.Role != MembershipRole.Owner))
        {
            return BusinessErrors.AccessDenied;
        }

        var business = await businesses.GetAsync(businessId, cancellationToken).ConfigureAwait(false);
        return business is null ? BusinessErrors.AccessDenied : new BusinessAccessGrant(business, membership.Role);
    }
}

/// <summary>İstifadəçinin biznes rollarını (JWT üçün) onun aktiv üzvlüklərinə uyğunlaşdırır.</summary>
public sealed class BusinessAccessSync(IUserRepository users, IBusinessMembershipRepository memberships)
{
    /// <summary>Aktivləşdirilən biznesin müraciətçisini sahib edir (üzvlük artıq varsa təkrar yaratmır).</summary>
    public async Task EnsureOwnerAsync(Business business, User owner, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(owner);
        if (await memberships.FindAsync(business.Id, owner.Id.Value, cancellationToken).ConfigureAwait(false) is null)
        {
            await memberships.AddAsync(BusinessMembership.Create(business.Id, owner.Id.Value, MembershipRole.Owner, at, null), cancellationToken)
                .ConfigureAwait(false);
        }

        await RecomputeAsync(owner, at, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolları üzvlüklərdən yenidən hesablayır: sahib olduğu biznes → <c>BusinessOwner</c>, işçi olduğu → <c>BusinessStaff</c>.
    /// Əsas biznes (<c>users.businessId</c>) hələ üzvü olduğu biznesdirsə qalır, yoxsa ən köhnə üzvlüyə keçir.
    /// </summary>
    public async Task RecomputeAsync(User user, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await memberships.ListForUserAsync(user.Id.Value, cancellationToken).ConfigureAwait(false);
        var primary = all.Any(m => m.BusinessId.Value == user.BusinessId) ? user.BusinessId : all.FirstOrDefault()?.BusinessId.Value;
        var changed = user.SyncBusinessRoles(
            all.Any(m => m.Role == MembershipRole.Owner), all.Any(m => m.Role == MembershipRole.Staff), primary, at, null);
        if (changed)
        {
            await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        }
    }
}
