using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Plus;

public readonly record struct CouponCodeId(Guid Value) : IStronglyTypedId<CouponCodeId>
{
    public static CouponCodeId From(Guid value) => new(value);
}

/// <summary>
/// <see cref="CouponCodeType.Unique"/> kuponun bir kodu (<c>couponCodes</c>). (<c>couponId</c>, <c>code</c>) partial unikaldır. İstifadəçiyə təyinat
/// tək şərtli atomik yeniləmədir (<c>assignedUserId: null</c> → istifadəçi); <see cref="Slot"/> istifadəçinin bu kupondakı neçənci kodudur və
/// (<c>couponId</c>, <c>assignedUserId</c>, <c>slot</c>) unikal indeksi paralel sorğularda da <c>perUserLimit</c>-in aşılmasına imkan vermir.
/// </summary>
public sealed class CouponCode : AggregateRoot<CouponCodeId>
{
    private CouponCode(CouponCodeId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private CouponCode()
    {
    }

    public CouponId CouponId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public Guid? AssignedUserId { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    /// <summary>1-dən <c>perUserLimit</c>-ə qədər; təyin olunmayıbsa <c>null</c>.</summary>
    public int? Slot { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Kodu yoxlayıb istifadə olunmuş kimi işarələyən brend istifadəçisi.</summary>
    public Guid? UsedBy { get; private set; }

    public static CouponCode Create(CouponId couponId, string code, DateTimeOffset at, Guid? by) =>
        new(StronglyTypedId.New<CouponCodeId>(), Guard.Utc(at), by)
        {
            CouponId = couponId,
            Code = CouponCodes.Normalize(code) ?? throw new ArgumentException("Kod düzgün deyil.", nameof(code)),
        };
}
