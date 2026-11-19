using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Businesses;

public readonly record struct BusinessMembershipId(Guid Value) : IStronglyTypedId<BusinessMembershipId>
{
    public static BusinessMembershipId From(Guid value) => new(value);
}

/// <summary>İstifadəçinin biznesdəki rolu: sahib hər şeyi idarə edir, işçi yalnız təhvil və günlük baxış.</summary>
public enum MembershipRole
{
    Owner,
    Staff,
}

/// <summary>
/// İstifadəçi ↔ biznes bağlantısı (<c>businessMemberships</c>). Bir istifadəçi bir neçə biznesdə (şəbəkə) üzv ola bilər.
/// İşçinin çıxarılması bu bağlantının soft delete-idir; giriş yoxlamaları JWT-dən yox, bu kolleksiyadan aparılır.
/// </summary>
public sealed class BusinessMembership : AggregateRoot<BusinessMembershipId>
{
    private BusinessMembership(BusinessMembershipId id, BusinessId businessId, Guid userId, MembershipRole role, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        BusinessId = businessId;
        UserId = userId;
        Role = role;
    }

    private BusinessMembership()
    {
    }

    public BusinessId BusinessId { get; private set; }

    public Guid UserId { get; private set; }

    public MembershipRole Role { get; private set; }

    public static BusinessMembership Create(BusinessId businessId, Guid userId, MembershipRole role, DateTimeOffset at, Guid? by) =>
        Enum.IsDefined(role)
            ? new BusinessMembership(StronglyTypedId.New<BusinessMembershipId>(), businessId, Guard.NotEmpty(userId), role, at, by)
            : throw new ArgumentOutOfRangeException(nameof(role));
}
