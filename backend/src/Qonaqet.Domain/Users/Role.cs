namespace Qonaqet.Domain.Users;

/// <summary>İstifadəçi rolları (<c>users.roles[]</c>). JWT-də <c>role</c> claim-i kimi bu adlarla gedir.</summary>
public enum Role
{
    User,
    BusinessStaff,
    BusinessOwner,
    CorporateUser,
    BrandUser,
    Admin,
}

/// <summary>Rol adları sabit kimi (<c>[RequiresRole(RoleNames.Admin)]</c> atributları üçün).</summary>
public static class RoleNames
{
    public const string User = nameof(Role.User);
    public const string BusinessStaff = nameof(Role.BusinessStaff);
    public const string BusinessOwner = nameof(Role.BusinessOwner);
    public const string CorporateUser = nameof(Role.CorporateUser);
    public const string BrandUser = nameof(Role.BrandUser);
    public const string Admin = nameof(Role.Admin);
}
