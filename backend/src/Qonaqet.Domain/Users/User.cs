using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçi (<c>users</c>). Qeydiyyat email OTP ilə olur: ilk girişdə hesab yalnız email ilə yaranır,
/// ad sonra profil addımında verilir (<see cref="ProfileCompleted"/>).
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public const int NameMaxLength = 100;

    private List<Role> _roles = [];

    private User(UserId id, string email, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Email = email;
        _roles.Add(Role.User);
    }

    private User()
    {
    }

    public string Email { get; private set; } = string.Empty;

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public Guid? AvatarFileId { get; private set; }

    public IReadOnlyList<Role> Roles
    {
        get => _roles;
        private set => _roles = [.. value];
    }

    /// <summary>Biznes rolları üçün bağlı biznes (E04 təyin edir).</summary>
    public Guid? BusinessId { get; private set; }

    /// <summary>Korporativ istifadəçi üçün şirkət (E10 təyin edir).</summary>
    public Guid? CorporateId { get; private set; }

    public bool ProfileCompleted => !string.IsNullOrWhiteSpace(FirstName);

    /// <summary>OTP ilə təsdiqlənmiş email üçün yeni hesab (rol: <see cref="Role.User"/>).</summary>
    public static User Register(string email, DateTimeOffset at) =>
        new(StronglyTypedId.New<UserId>(), EmailAddress.Normalize(email), at);

    public bool HasRole(Role role) => _roles.Contains(role);

    /// <summary>Rolları əvəz edir. <see cref="Role.User"/> həmişə qalır, təkrarlar atılır.</summary>
    public void SetRoles(IEnumerable<Role> roles, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles = [Role.User, .. roles.Where(r => r != Role.User).Distinct().Order()];
        MarkUpdated(at, by);
    }
}
