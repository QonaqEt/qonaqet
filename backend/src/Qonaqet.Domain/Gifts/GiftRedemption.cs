using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Gifts;

public readonly record struct GiftRedemptionId(Guid Value) : IStronglyTypedId<GiftRedemptionId>
{
    public static GiftRedemptionId From(Guid value) => new(value);
}

/// <summary>
/// Hədiyyənin bir təhvili (<c>giftRedemptions</c>, append-only): heç vaxt dəyişmir və silinmir; düzəliş tərs yazı ilə olur (E13 reverse).
/// Məhsul hədiyyəsində sətirlər, vauçerdə məbləğ saxlanılır; <see cref="ValueQepik"/> hər ikisində təhvilin dəyəridir.
/// </summary>
public sealed class GiftRedemption : AggregateRoot<GiftRedemptionId>, IAppendOnly
{
    private List<GiftLine> _lines = [];

    private GiftRedemption(GiftRedemptionId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private GiftRedemption()
    {
    }

    public GiftId GiftId { get; private set; }

    public BusinessId BusinessId { get; private set; }

    public Guid StaffUserId { get; private set; }

    public IReadOnlyList<GiftLine> Lines
    {
        get => _lines;
        private set => _lines = [.. value];
    }

    /// <summary>Vauçerdə təhvil olunan məbləğ; məhsul hədiyyəsində <c>null</c>.</summary>
    public long? AmountQepik { get; private set; }

    public long ValueQepik { get; private set; }

    internal static GiftRedemption Record(
        GiftId giftId, BusinessId businessId, Guid staffUserId, IReadOnlyList<GiftLine> lines, long? amountQepik, long valueQepik, DateTimeOffset at) =>
        new(StronglyTypedId.New<GiftRedemptionId>(), Guard.Utc(at), staffUserId)
        {
            GiftId = giftId,
            BusinessId = businessId,
            StaffUserId = staffUserId,
            Lines = lines,
            AmountQepik = amountQepik,
            ValueQepik = Guard.Positive(valueQepik),
        };
}
