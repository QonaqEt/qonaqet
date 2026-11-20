using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Payments;

public readonly record struct PaymentId(Guid Value) : IStronglyTypedId<PaymentId>
{
    public static PaymentId From(Guid value) => new(value);
}

/// <summary>Ödənişin məqsədi; təsdiqdən sonra hər məqsədi öz consumer-i yerinə yetirir (hədiyyə → <c>Scheduled</c>, hovuz krediti …).</summary>
public enum PaymentPurpose
{
    Gift,
    CorporateGiftOrder,
    Remote,
    Pool,
    Campaign,
    PartnerTopUp,
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Expired,
    Refunded,
    PartiallyRefunded,
}

/// <summary>
/// Onlayn ödəniş (Postgres <c>payments</c>). Yalnız status dəyişikliyi ilə yenilənir, heç vaxt silinmir (append-only).
/// Məbləğ serverdə hesablanır və yaradılandan sonra dəyişmir. Status yalnız provayderin Get Order cavabı ilə dəyişir
/// (callback imzasızdır). <see cref="Version"/> optimistik paralellik nişanıdır.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>, IAppendOnly
{
    public const int MaxDescriptionLength = 200;

    private Payment(
        PaymentId id,
        PaymentPurpose purpose,
        Guid referenceId,
        long amount,
        string provider,
        string providerOrderId,
        Uri paymentUrl,
        string description,
        PaymentPayer payer,
        DateTimeOffset createdAt)
        : base(id, createdAt, payer.UserId)
    {
        Purpose = purpose;
        ReferenceId = Guard.NotEmpty(referenceId);
        Amount = Guard.Positive(amount);
        Provider = Guard.NotNullOrWhiteSpace(provider);
        ProviderOrderId = Guard.NotNullOrWhiteSpace(providerOrderId);
        PaymentUrl = Guard.NotNull(paymentUrl).ToString();
        Description = Guard.NotNullOrWhiteSpace(description).Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description;
        PayerUserId = payer.UserId;
        CorporateId = payer.CorporateId;
        PartnerClientId = payer.PartnerClientId;
        Status = PaymentStatus.Pending;
        Payload = "{}";
    }

    private Payment()
    {
    }

    public Guid? PayerUserId { get; private set; }

    public Guid? CorporateId { get; private set; }

    public Guid? PartnerClientId { get; private set; }

    public PaymentPurpose Purpose { get; private set; }

    /// <summary>Məqsədin obyekti (hədiyyə, korporativ sifariş, hovuz …).</summary>
    public Guid ReferenceId { get; private set; }

    /// <summary>Qəpik.</summary>
    public long Amount { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderOrderId { get; private set; } = string.Empty;

    public string PaymentUrl { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    /// <summary>Uğurla qaytarılmış cəm (qəpik).</summary>
    public long RefundedAmount { get; private set; }

    /// <summary>Provayderin son cavabının qısa snapshot-u (JSON; kart məlumatı yoxdur).</summary>
    public string Payload { get; private set; } = "{}";

    public DateTimeOffset? PaidAt { get; private set; }

    public int Version { get; private set; }

    public long RefundableAmount => Status is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded ? Amount - RefundedAmount : 0;

    public static Payment Create(
        PaymentPurpose purpose,
        Guid referenceId,
        long amount,
        string provider,
        string providerOrderId,
        Uri paymentUrl,
        string description,
        PaymentPayer payer,
        DateTimeOffset at) =>
        new(StronglyTypedId.New<PaymentId>(), purpose, referenceId, amount, provider, providerOrderId, paymentUrl, description, payer, at);

    /// <summary>
    /// Provayder ödənişi təsdiqlədi. <c>Pending</c>-dən, həm də gecikmiş təsdiqdə <c>Expired</c>/<c>Failed</c>-dən keçir (pul artıq
    /// tutulub; məqsədin consumer-i lazım gələrsə refund edir). Təkrar çağırış heç nə dəyişmir → <c>false</c>.
    /// </summary>
    public bool MarkPaid(DateTimeOffset at, string providerStatus)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Expired or PaymentStatus.Failed))
        {
            return false;
        }

        Status = PaymentStatus.Paid;
        PaidAt = Guard.Utc(at);
        Touch(at, providerStatus);
        Raise(new PaymentConfirmed(Id, Purpose, ReferenceId, Amount, PayerUserId, CorporateId, PartnerClientId, at));
        return true;
    }

    /// <summary>Provayder rədd etdi (yalnız <c>Pending</c>-dən).</summary>
    public bool MarkFailed(DateTimeOffset at, string providerStatus) => Close(PaymentStatus.Failed, at, providerStatus);

    /// <summary>30 dəqiqə ərzində ödənilmədi (yalnız <c>Pending</c>-dən).</summary>
    public bool MarkExpired(DateTimeOffset at, string providerStatus) => Close(PaymentStatus.Expired, at, providerStatus);

    /// <summary>Refund sorğusu qeydə alındı: versiya artır ki, eyni anda ikinci sorğu eyni qalığı "görməsin".</summary>
    public void NoteRefundRequested(DateTimeOffset at) => Touch(at, providerStatus: null);

    /// <summary>Uğurlu refundu tətbiq edir: <c>Refunded</c> (tam) və ya <c>PartiallyRefunded</c>.</summary>
    public void ApplyRefund(RefundId refundId, long amount, DateTimeOffset at)
    {
        Guard.Positive(amount);
        if (amount > RefundableAmount)
        {
            throw new InvalidOperationException("Refund ödənişin qalan hissəsini aşır.");
        }

        RefundedAmount += amount;
        Status = RefundedAmount == Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch(at, providerStatus: null);
        Raise(new PaymentRefunded(Id, refundId, Purpose, ReferenceId, amount, RefundedAmount, at));
    }

    private bool Close(PaymentStatus status, DateTimeOffset at, string providerStatus)
    {
        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = status;
        Touch(at, providerStatus);
        Raise(new PaymentClosedUnpaid(Id, Purpose, ReferenceId, status, at));
        return true;
    }

    private void Touch(DateTimeOffset at, string? providerStatus)
    {
        if (providerStatus is not null)
        {
            Payload = System.Text.Json.JsonSerializer.Serialize(new { providerStatus });
        }

        Version++;
        MarkUpdated(at, null);
    }
}

/// <summary>Kim ödəyir: istifadəçi, şirkət və ya partnyor klienti (ən azı biri).</summary>
public sealed record PaymentPayer(Guid? UserId, Guid? CorporateId = null, Guid? PartnerClientId = null);

/// <summary>Ödəniş təsdiqləndi (Postgres outbox-u ilə eyni tranzaksiyada). Hər məqsədin consumer-i idempotent yerinə yetirir.</summary>
public sealed record PaymentConfirmed(
    PaymentId PaymentId,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    long Amount,
    Guid? PayerUserId,
    Guid? CorporateId,
    Guid? PartnerClientId,
    DateTimeOffset At) : DomainEvent(At);

/// <summary>Ödəniş uğursuz oldu və ya müddəti bitdi (məqsəd, məs. hədiyyə, ləğv oluna bilər).</summary>
public sealed record PaymentClosedUnpaid(PaymentId PaymentId, PaymentPurpose Purpose, Guid ReferenceId, PaymentStatus Status, DateTimeOffset At) : DomainEvent(At);

/// <summary>Uğurlu refund (ledger-də <c>Refund</c> yazısı).</summary>
public sealed record PaymentRefunded(
    PaymentId PaymentId,
    RefundId RefundId,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    long Amount,
    long TotalRefunded,
    DateTimeOffset At) : DomainEvent(At);
