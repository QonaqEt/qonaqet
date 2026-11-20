using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Payments;

public readonly record struct RefundId(Guid Value) : IStronglyTypedId<RefundId>
{
    public static RefundId From(Guid value) => new(value);
}

public enum RefundStatus
{
    /// <summary>Qeydə alındı, provayderə göndərilir.</summary>
    Requested,

    Succeeded,

    Failed,
}

/// <summary>
/// Refund (Postgres <c>refunds</c>). Əvvəl <c>Requested</c> kimi yazılır, sonra provayder çağırılır: proses yarıda qalsa belə iz qalır və
/// eyni anda ikinci refund qalığı aşa bilmir. Silinmir.
/// </summary>
public sealed class Refund : Entity<RefundId>
{
    public const int MaxReasonLength = 300;

    private Refund(RefundId id, PaymentId paymentId, long amount, string reason, Guid? requestedBy, DateTimeOffset at)
        : base(id, at, requestedBy)
    {
        PaymentId = paymentId;
        Amount = Guard.Positive(amount);
        reason = Guard.NotNullOrWhiteSpace(reason).Trim();
        Reason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        RequestedBy = requestedBy;
        Status = RefundStatus.Requested;
    }

    private Refund()
    {
    }

    public PaymentId PaymentId { get; private set; }

    public long Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>Provayderin cavab ID-si (uzlaşdırma üçün).</summary>
    public string? ProviderRef { get; private set; }

    public RefundStatus Status { get; private set; }

    /// <summary>Admin; avtomatik refundda (hədiyyə müddəti, ləğv) <c>null</c>.</summary>
    public Guid? RequestedBy { get; private set; }

    public string? FailureReason { get; private set; }

    public static Refund Request(PaymentId paymentId, long amount, string reason, Guid? requestedBy, DateTimeOffset at) =>
        new(StronglyTypedId.New<RefundId>(), paymentId, amount, reason, requestedBy, at);

    public void Succeed(string? providerRef, DateTimeOffset at)
    {
        EnsureRequested();
        Status = RefundStatus.Succeeded;
        ProviderRef = providerRef;
        MarkUpdated(at, null);
    }

    public void Fail(string reason, DateTimeOffset at)
    {
        EnsureRequested();
        Status = RefundStatus.Failed;
        FailureReason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        MarkUpdated(at, null);
    }

    private void EnsureRequested()
    {
        if (Status != RefundStatus.Requested)
        {
            throw new InvalidOperationException("Refund artıq yekunlaşıb.");
        }
    }
}
