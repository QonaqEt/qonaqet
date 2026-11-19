namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Sorğunu edən aktor (istifadəçi və ya inteqrasiya klienti). Host-lar JWT-dən doldurur (E02).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>İstifadəçi və ya inteqrasiya klientinin ID-si.</summary>
    Guid? ActorId { get; }

    IReadOnlySet<string> Roles { get; }

    /// <summary>Biznes rollu aktorun biznesi (JWT <c>business_id</c>); yoxdursa <c>null</c>.</summary>
    Guid? BusinessId { get; }

    /// <summary>Korporativ aktorun şirkəti (JWT <c>corporate_id</c>); yoxdursa <c>null</c>.</summary>
    Guid? CorporateId { get; }
}

/// <summary>
/// Handler-lərdəki resurs yoxlamaları ("bu staff bu biznesə aiddir"). Pipeline yalnız rolu yoxlayır; konkret resursa
/// giriş handler-də bu köməkçilərlə yoxlanılır və uğursuzluq <c>forbidden</c> qaytarır.
/// </summary>
public static class CurrentUserExtensions
{
    public static bool IsInRole(this ICurrentUser user, string role)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Roles.Contains(role);
    }

    public static bool IsAdmin(this ICurrentUser user) => user.IsInRole(Domain.Users.RoleNames.Admin);

    /// <summary>Admin hər biznes üçün; staff/sahib yalnız öz biznesi üçün hərəkət edə bilər.</summary>
    public static bool CanActForBusiness(this ICurrentUser user, Guid businessId)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.IsAdmin())
        {
            return true;
        }

        return user.BusinessId == businessId
            && (user.IsInRole(Domain.Users.RoleNames.BusinessOwner) || user.IsInRole(Domain.Users.RoleNames.BusinessStaff));
    }

    /// <summary>Admin hər şirkət üçün; korporativ istifadəçi yalnız öz şirkəti üçün.</summary>
    public static bool CanActForCorporate(this ICurrentUser user, Guid corporateId)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsAdmin() || (user.CorporateId == corporateId && user.IsInRole(Domain.Users.RoleNames.CorporateUser));
    }
}

/// <summary>Hesabın vəziyyəti (<c>[RequiresActiveAccount]</c> yoxlaması üçün).</summary>
public enum AccountStatus
{
    Active,
    Blocked,
    Missing,
}

public interface IAccountStatusReader
{
    Task<AccountStatus> GetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Sorğu səviyyəli kontekst (HTTP başlıqlarından).</summary>
public interface IRequestContext
{
    /// <summary><c>Idempotency-Key</c> başlığı; yoxdursa <c>null</c>.</summary>
    string? IdempotencyKey { get; }
}
