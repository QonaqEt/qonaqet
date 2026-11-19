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
