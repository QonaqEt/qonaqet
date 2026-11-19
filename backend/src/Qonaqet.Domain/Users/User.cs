using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçi (<c>users</c>). Qeydiyyat email OTP ilə olur: ilk girişdə hesab yalnız email ilə yaranır,
/// ad sonra profil addımında verilir (<see cref="ProfileCompleted"/>).
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public const int NameMaxLength = 100;

    /// <summary>Bir hesabda ən çox bu qədər cihaz token-i saxlanılır; yenisi gələndə ən köhnəsi düşür.</summary>
    public const int MaxFcmTokens = 10;

    private List<Role> _roles = [];
    private List<string> _fcmTokens = [];
    private List<NotificationOptOut> _notificationOptOuts = [];

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

    /// <summary>Push üçün cihaz token-ləri (köhnədən yeniyə).</summary>
    public IReadOnlyList<string> FcmTokens
    {
        get => _fcmTokens;
        private set => _fcmTokens = [.. value];
    }

    /// <summary>Söndürülmüş bildirişlər (növ + kanal); siyahıda olmayan hər şey açıqdır.</summary>
    public IReadOnlyList<NotificationOptOut> NotificationOptOuts
    {
        get => _notificationOptOuts;
        private set => _notificationOptOuts = [.. value];
    }

    /// <summary>Admin tərəfindən bloklanıb: giriş, sessiya yeniləmə və <c>[RequiresActiveAccount]</c> əmrləri qadağandır.</summary>
    public bool IsBlocked { get; private set; }

    public DateTimeOffset? BlockedAt { get; private set; }

    public string? BlockReason { get; private set; }

    public bool ProfileCompleted => !string.IsNullOrWhiteSpace(FirstName);

    /// <summary>OTP ilə təsdiqlənmiş email üçün yeni hesab (rol: <see cref="Role.User"/>).</summary>
    public static User Register(string email, DateTimeOffset at) =>
        new(StronglyTypedId.New<UserId>(), EmailAddress.Normalize(email), at);

    public bool HasRole(Role role) => _roles.Contains(role);

    /// <summary>Profil: ad məcburidir, soyad boş sətirlə silinir; avatar fayl ID-si ilə (null = avatarı sil).</summary>
    public void UpdateProfile(string firstName, string? lastName, Guid? avatarFileId, DateTimeOffset at)
    {
        FirstName = Guard.NotNullOrWhiteSpace(firstName).Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
        AvatarFileId = avatarFileId == Guid.Empty ? null : avatarFileId;
        MarkUpdated(at, Id.Value);
    }

    /// <summary>Cihaz token-i əlavə edir; artıq varsa dublikat yaratmır, sadəcə ən yeni kimi sona keçir.</summary>
    public void AddFcmToken(string token, DateTimeOffset at)
    {
        Guard.NotNullOrWhiteSpace(token);
        _fcmTokens.Remove(token);
        _fcmTokens.Add(token);
        if (_fcmTokens.Count > MaxFcmTokens)
        {
            _fcmTokens.RemoveRange(0, _fcmTokens.Count - MaxFcmTokens);
        }

        MarkUpdated(at, Id.Value);
    }

    public bool RemoveFcmToken(string token, DateTimeOffset at)
    {
        if (!_fcmTokens.Remove(token))
        {
            return false;
        }

        MarkUpdated(at, Id.Value);
        return true;
    }

    public bool AllowsNotification(NotificationType type, NotificationChannel channel) =>
        !_notificationOptOuts.Contains(new NotificationOptOut(type, channel));

    /// <summary>Bildiriş növünü kanal üzrə açır/söndürür.</summary>
    public void SetNotificationPreference(NotificationType type, NotificationChannel channel, bool enabled, DateTimeOffset at)
    {
        var optOut = new NotificationOptOut(type, channel);
        _notificationOptOuts.Remove(optOut);
        if (!enabled)
        {
            _notificationOptOuts.Add(optOut);
        }

        MarkUpdated(at, Id.Value);
    }

    /// <summary>Bloklayır və <see cref="UserBlocked"/> qaldırır; artıq bloklanıbsa heç nə etmir.</summary>
    public bool Block(string reason, DateTimeOffset at, Guid? by)
    {
        if (IsBlocked)
        {
            return false;
        }

        IsBlocked = true;
        BlockedAt = Guard.Utc(at);
        BlockReason = Guard.NotNullOrWhiteSpace(reason).Trim();
        MarkUpdated(at, by);
        Raise(new UserBlocked(Id, at));
        return true;
    }

    public bool Unblock(DateTimeOffset at, Guid? by)
    {
        if (!IsBlocked)
        {
            return false;
        }

        IsBlocked = false;
        BlockedAt = null;
        BlockReason = null;
        MarkUpdated(at, by);
        Raise(new UserUnblocked(Id, at));
        return true;
    }

    /// <summary>Rolları əvəz edir. <see cref="Role.User"/> həmişə qalır, təkrarlar atılır.</summary>
    public void SetRoles(IEnumerable<Role> roles, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles = [Role.User, .. roles.Where(r => r != Role.User).Distinct().Order()];
        MarkUpdated(at, by);
    }
}
