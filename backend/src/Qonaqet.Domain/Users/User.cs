using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçi (<c>users</c>). Qeydiyyat email OTP ilə olur: ilk girişdə hesab yalnız email ilə yaranır,
/// ad sonra profil addımında verilir (<see cref="ProfileCompleted"/>).
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public const int NameMaxLength = 100;

    /// <summary>Reytinq ləqəbinin uzunluğu.</summary>
    public const int LeaderboardNameMinLength = 2;

    public const int LeaderboardNameMaxLength = 30;

    /// <summary>Bir hesabda ən çox bu qədər cihaz token-i saxlanılır; yenisi gələndə ən köhnəsi düşür.</summary>
    public const int MaxFcmTokens = 10;

    private List<Role> _roles = [];
    private List<string> _fcmTokens = [];
    private List<NotificationOptOut> _notificationOptOuts = [];
    private List<VerifiedCategory> _verifiedCategories = [];

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

    /// <summary>Reytinqdə görünmək (default açıq; istifadəçi istənilən vaxt söndürə bilər). Söndürülsə də xal qazanılır, sadəcə lövhədə görünmür.</summary>
    public bool LeaderboardOptIn { get; private set; } = true;

    /// <summary>Reytinq ləqəbi; yoxdursa lövhədə ad + soyadın ilk hərfi göstərilir.</summary>
    public string? LeaderboardName { get; private set; }

    /// <summary>Lövhədəki ad: ləqəb, yoxsa "Ad S.", ad hələ verilməyibsə "Qonaq". Email və tam soyad heç vaxt göstərilmir.</summary>
    public string LeaderboardDisplayName =>
        LeaderboardName
        ?? (string.IsNullOrWhiteSpace(FirstName) ? "Qonaq" : string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName[0]}.");

    /// <summary>Reytinq seçimləri: görünmə və ləqəb (<c>null</c> = ləqəbi sil).</summary>
    public void SetLeaderboardPreferences(bool optIn, string? leaderboardName, DateTimeOffset at)
    {
        var name = string.IsNullOrWhiteSpace(leaderboardName) ? null : leaderboardName.Trim();
        if (name is not null)
        {
            Guard.InRange(name.Length, LeaderboardNameMinLength, LeaderboardNameMaxLength);
        }

        LeaderboardOptIn = optIn;
        LeaderboardName = name;
        MarkUpdated(at, Id.Value);
    }

    /// <summary>
    /// Təsdiqlənmiş kateqoriyalar (hər kateqoriyaya bir qeyd). Müddət məntiqidir: <see cref="VerifiedCategory.IsValidAt"/>
    /// <c>validUntil</c>-i indiki anla müqayisə edir, bitmiş qeyd heç bir hüquq vermir.
    /// </summary>
    public IReadOnlyList<VerifiedCategory> VerifiedCategories
    {
        get => _verifiedCategories;
        private set => _verifiedCategories = [.. value];
    }

    public VerifiedCategory? VerifiedCategoryFor(Guid categoryId) => _verifiedCategories.FirstOrDefault(v => v.CategoryId == categoryId);

    /// <summary>Kateqoriya təsdiqi yazılır; həmin kateqoriyanın köhnə qeydi (yeniləmə) əvəz olunur.</summary>
    public void VerifyCategory(Guid categoryId, string? subtype, DateTimeOffset? validUntil, Guid verificationId, DateTimeOffset at, Guid? by)
    {
        _verifiedCategories.RemoveAll(v => v.CategoryId == categoryId);
        _verifiedCategories.Add(VerifiedCategory.Create(categoryId, subtype, validUntil, verificationId, at));
        MarkUpdated(at, by);
    }

    /// <summary>Admin <c>validUntil</c>-i əl ilə dəyişir (<c>null</c> = müddətsiz). Bu təsdiq istifadəçidə yoxdursa <c>false</c>.</summary>
    public bool OverrideCategoryValidity(Guid verificationId, DateTimeOffset? validUntil, DateTimeOffset at, Guid? by)
    {
        var entry = _verifiedCategories.FirstOrDefault(v => v.VerificationId == verificationId);
        if (entry is null)
        {
            return false;
        }

        entry.ChangeValidUntil(validUntil);
        MarkUpdated(at, by);
        return true;
    }

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

    /// <summary>
    /// Biznes rollarını bizneslərdəki üzvlüklərə uyğunlaşdırır: sahib olduğu biznes varsa <see cref="Role.BusinessOwner"/>,
    /// işçi olduğu biznes varsa <see cref="Role.BusinessStaff"/>, yoxdursa rol götürülür. <see cref="BusinessId"/> ilk
    /// (əsas) biznesdir — yalnız köhnə tək-biznes klientləri üçün; giriş yoxlamaları üzvlüklərlə aparılır.
    /// </summary>
    public bool SyncBusinessRoles(bool isOwner, bool isStaff, Guid? primaryBusinessId, DateTimeOffset at, Guid? by)
    {
        var roles = _roles.Where(r => r is not (Role.BusinessOwner or Role.BusinessStaff)).ToList();
        if (isOwner)
        {
            roles.Add(Role.BusinessOwner);
        }

        if (isStaff)
        {
            roles.Add(Role.BusinessStaff);
        }

        var ordered = roles.Distinct().Order().ToList();
        if (ordered.SequenceEqual(_roles.Order()) && BusinessId == primaryBusinessId)
        {
            return false;
        }

        _roles = ordered;
        BusinessId = primaryBusinessId;
        MarkUpdated(at, by);
        return true;
    }

    /// <summary>
    /// İstifadəçini şirkətə bağlayır və <see cref="Role.CorporateUser"/> rolu verir; <c>null</c> bağlılığı və rolu götürür. Dəyişiklik yoxdursa
    /// <c>false</c>.
    /// </summary>
    public bool AssignCorporate(Guid? corporateId, DateTimeOffset at, Guid? by)
    {
        var roles = _roles.Where(r => r != Role.CorporateUser).ToList();
        if (corporateId is not null)
        {
            roles.Add(Role.CorporateUser);
        }

        var ordered = roles.Distinct().Order().ToList();
        if (ordered.SequenceEqual(_roles.Order()) && CorporateId == corporateId)
        {
            return false;
        }

        _roles = ordered;
        CorporateId = corporateId;
        MarkUpdated(at, by);
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
