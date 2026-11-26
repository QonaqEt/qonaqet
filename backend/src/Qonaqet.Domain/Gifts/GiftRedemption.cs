using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Gifts;

public readonly record struct GiftRedemptionId(Guid Value) : IStronglyTypedId<GiftRedemptionId>
{
    public static GiftRedemptionId From(Guid value) => new(value);
}

/// <summary>
/// Hədiyyənin bir təhvili (<c>giftRedemptions</c>, append-only): heç vaxt dəyişmir və silinmir; düzəliş tərs yazı ilə olur (E13 reverse:
/// <see cref="ReversesRedemptionId"/>, mənfi dəyər).
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

    /// <summary>Təhvilin dəyəri; tərs yazıda mənfidir (cəmlər avtomatik xalis olur).</summary>
    public long ValueQepik { get; private set; }

    /// <summary>Tərs yazıda geri alınan təhvilin ID-si; adi təhvildə <c>null</c>. Bir təhvil ən çox bir dəfə geri alınır (unikal indeks).</summary>
    public GiftRedemptionId? ReversesRedemptionId { get; private set; }

    public bool IsReversal => ReversesRedemptionId is not null;

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

    internal static GiftRedemption RecordReversal(GiftRedemption original, Guid actorId, DateTimeOffset at) =>
        new(StronglyTypedId.New<GiftRedemptionId>(), Guard.Utc(at), actorId)
        {
            GiftId = original.GiftId,
            BusinessId = original.BusinessId,
            StaffUserId = actorId,
            Lines = original.Lines,
            AmountQepik = -original.AmountQepik,
            ValueQepik = -original.ValueQepik,
            ReversesRedemptionId = original.Id,
        };
}
