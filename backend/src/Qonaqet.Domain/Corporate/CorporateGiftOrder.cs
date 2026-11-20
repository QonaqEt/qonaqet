using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Corporate;

public readonly record struct CorporateGiftOrderId(Guid Value) : IStronglyTypedId<CorporateGiftOrderId>
{
    public static CorporateGiftOrderId From(Guid value) => new(value);
}

public enum CorporateOrderStatus
{
    /// <summary>Şablon hazırlanır, alıcı siyahısı yüklənir.</summary>
    Draft,

    /// <summary>Kartla ödəniş və ya bank köçürməsinin təsdiqi gözlənilir.</summary>
    AwaitingPayment,

    /// <summary>Ödənilib, hədiyyələr hələ yaradılmayıb.</summary>
    Paid,

    /// <summary>Hədiyyələr paketlərlə yaradılır.</summary>
    Sending,

    Completed,

    Cancelled,
}

public enum CorporatePaymentMethod
{
    Card,
    BankTransfer,
}

/// <summary>Sifarişin alıcısı (CSV sətri): ad və email.</summary>
public sealed record CorporateRecipient(string Name, string Email);

/// <summary>
/// Korporativ hədiyyə/bonus sifarişi (<c>corporateGiftOrders</c>): bir şablon (məhsullar, biznes vauçeri və ya şəbəkə vauçeri; tema həmişə
/// <c>Corporate</c>), mesaj, alıcı siyahısı (CSV, ≤ 5,000) və ödəniş (kart və ya admin təsdiqli bank köçürməsi). Ödənişdən sonra fan-out job-u hər
/// alıcı üçün bir hədiyyə yaradır; <see cref="SentCount"/> yarıda kəsilən işin qaldığı yerdir.
/// </summary>
public sealed class CorporateGiftOrder : AggregateRoot<CorporateGiftOrderId>
{
    public const int MaxRecipients = 5_000;

    private List<GiftItem> _items = [];
    private List<CorporateRecipient> _recipients = [];

    private CorporateGiftOrder(CorporateGiftOrderId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private CorporateGiftOrder()
    {
    }

    public CorporateId CorporateId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public GiftKind Kind { get; private set; }

    public GiftScope Scope { get; private set; }

    public BusinessId? BusinessId { get; private set; }

    /// <summary>Hər hədiyyənin məhsulları (snapshot).</summary>
    public IReadOnlyList<GiftItem> Items
    {
        get => _items;
        private set => _items = [.. value];
    }

    /// <summary>Bir hədiyyənin qiyməti (serverdə hesablanıb).</summary>
    public long UnitPriceQepik { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset DeliverAt { get; private set; }

    public GiftExpiryAction ExpiryAction { get; private set; }

    public PoolId? ExpiryPoolId { get; private set; }

    public IReadOnlyList<CorporateRecipient> Recipients
    {
        get => _recipients;
        private set => _recipients = [.. value];
    }

    /// <summary>Yüklənmiş CSV faylı (<c>files</c>, məqsəd <c>RecipientCsv</c>).</summary>
    public Guid? RecipientsFileId { get; private set; }

    public int RecipientCount { get; private set; }

    /// <summary>Ümumi məbləğ = hədiyyə qiyməti × alıcı sayı.</summary>
    public long TotalQepik { get; private set; }

    public CorporatePaymentMethod? PaymentMethod { get; private set; }

    public Guid? PaymentId { get; private set; }

    public string? BankReference { get; private set; }

    public CorporateOrderStatus Status { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>Hədiyyəsi yaradılmış alıcıların sayı (sıra ilə: ilk <see cref="SentCount"/> alıcı).</summary>
    public int SentCount { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static CorporateGiftOrder Create(
        CorporateId corporateId, Guid createdByUserId, GiftContent content, string? message, DateTimeOffset deliverAt, GiftExpiryPlan expiry,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(expiry);
        if (message is { Length: > GiftRules.MessageMaxLength })
        {
            throw new ArgumentException("Mesaj ən çox 500 simvol ola bilər.", nameof(message));
        }

        if (expiry.Action == GiftExpiryAction.DonateToPool && expiry.PoolId is null)
        {
            throw new ArgumentException("Hovuza bağışlama üçün hovuz lazımdır.", nameof(expiry));
        }

        return new CorporateGiftOrder(StronglyTypedId.New<CorporateGiftOrderId>(), Guard.Utc(now), createdByUserId)
        {
            CorporateId = corporateId,
            CreatedByUserId = Guard.NotEmpty(createdByUserId),
            Kind = content.Kind,
            Scope = content.Scope,
            BusinessId = content.BusinessId,
            Items = content.Items,
            UnitPriceQepik = content.AmountQepik,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            DeliverAt = Guard.Utc(deliverAt) < now ? now : deliverAt,
            ExpiryAction = expiry.Action,
            ExpiryPoolId = expiry.PoolId,
            Status = CorporateOrderStatus.Draft,
        };
    }

    /// <summary>Yoxlanılmış alıcı siyahısı (yalnız qaralamada; yenidən yükləmə əvvəlkini əvəz edir). Məbləğ yenidən hesablanır.</summary>
    public void SetRecipients(IReadOnlyList<CorporateRecipient> recipients, Guid? fileId, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        EnsureStatus(CorporateOrderStatus.Draft);
        if (recipients.Count is 0 or > MaxRecipients)
        {
            throw new ArgumentException("Alıcı sayı 1–5000 olmalıdır.", nameof(recipients));
        }

        Recipients = recipients;
        RecipientsFileId = fileId;
        RecipientCount = recipients.Count;
        TotalQepik = checked(UnitPriceQepik * recipients.Count);
        MarkUpdated(at, by);
    }

    /// <summary>Kartla ödəniş başladı: <c>Draft</c> → <c>AwaitingPayment</c>.</summary>
    public void StartCardPayment(Guid paymentId, DateTimeOffset at, Guid? by)
    {
        EnsureReadyToPay();
        PaymentMethod = CorporatePaymentMethod.Card;
        PaymentId = Guard.NotEmpty(paymentId);
        Status = CorporateOrderStatus.AwaitingPayment;
        MarkUpdated(at, by);
    }

    /// <summary>Bank köçürməsi seçildi: admin bank referansı ilə təsdiqləyənə qədər <c>AwaitingPayment</c>.</summary>
    public void ChooseBankTransfer(DateTimeOffset at, Guid? by)
    {
        EnsureReadyToPay();
        if (ExpiryAction != GiftExpiryAction.DonateToPool)
        {
            // Bank köçürməsi provayderdən refund oluna bilməz: istifadə olunmayan dəyər hovuza bağışlanır.
            throw new InvalidOperationException("Bank köçürməsində müddət bitməsi yalnız hovuza bağışlamadır.");
        }

        PaymentMethod = CorporatePaymentMethod.BankTransfer;
        Status = CorporateOrderStatus.AwaitingPayment;
        MarkUpdated(at, by);
    }

    /// <summary>Ödəniş təsdiqləndi (kart: consumer; bank: admin). Təkrar çağırış <c>false</c>.</summary>
    public bool MarkPaid(string? bankReference, DateTimeOffset at, Guid? by)
    {
        if (Status != CorporateOrderStatus.AwaitingPayment)
        {
            return false;
        }

        if (PaymentMethod == CorporatePaymentMethod.BankTransfer)
        {
            BankReference = Guard.NotNullOrWhiteSpace(bankReference).Trim();
        }

        Status = CorporateOrderStatus.Paid;
        PaidAt = Guard.Utc(at);
        MarkUpdated(at, by);
        Raise(new CorporateOrderPaid(Id, CorporateId, TotalQepik, PaymentMethod ?? CorporatePaymentMethod.Card, at));
        return true;
    }

    /// <summary>Kart ödənişi baş tutmadı: sifariş qaralamaya qayıdır (yenidən ödəmək olar).</summary>
    public bool PaymentFailed(DateTimeOffset at)
    {
        if (Status != CorporateOrderStatus.AwaitingPayment || PaymentMethod != CorporatePaymentMethod.Card)
        {
            return false;
        }

        Status = CorporateOrderStatus.Draft;
        PaymentMethod = null;
        PaymentId = null;
        MarkUpdated(at, null);
        return true;
    }

    /// <summary>Ödənilməmiş sifarişin ləğvi.</summary>
    public void Cancel(DateTimeOffset at, Guid? by)
    {
        if (Status is not (CorporateOrderStatus.Draft or CorporateOrderStatus.AwaitingPayment))
        {
            throw new InvalidOperationException($"Sifariş {Status} vəziyyətindədir, ləğv edilə bilməz.");
        }

        Status = CorporateOrderStatus.Cancelled;
        MarkUpdated(at, by);
    }

    /// <summary>Hədiyyəsi yaradılmış alıcılar irəlilədi (fan-out paketi); son alıcıda <c>Completed</c>.</summary>
    public void AdvanceSent(int sentCount, DateTimeOffset at)
    {
        if (Status is not (CorporateOrderStatus.Paid or CorporateOrderStatus.Sending) || sentCount < SentCount || sentCount > RecipientCount)
        {
            throw new InvalidOperationException("Fan-out vəziyyəti düzgün deyil.");
        }

        SentCount = sentCount;
        Status = SentCount == RecipientCount ? CorporateOrderStatus.Completed : CorporateOrderStatus.Sending;
        CompletedAt = Status == CorporateOrderStatus.Completed ? at : null;
        MarkUpdated(at, null);
    }

    /// <summary>Şablondan bir alıcının hədiyyə məzmunu (məhsul sətirlərinin təzə nüsxəsi ilə).</summary>
    public GiftContent Content() => Kind switch
    {
        GiftKind.Items => GiftContent.ForItems(BusinessId!.Value, [.. _items.Select(i => GiftItem.Create(i.CatalogItemId, i.Title, i.UnitPriceQepik, i.Qty))]),
        _ when Scope == GiftScope.Network => GiftContent.ForNetworkVoucher(UnitPriceQepik),
        _ => GiftContent.ForBusinessVoucher(BusinessId!.Value, UnitPriceQepik),
    };

    private void EnsureReadyToPay()
    {
        EnsureStatus(CorporateOrderStatus.Draft);
        if (RecipientCount == 0)
        {
            throw new InvalidOperationException("Əvvəlcə alıcı siyahısını yükləyin.");
        }
    }

    private void EnsureStatus(CorporateOrderStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Sifariş {Status} vəziyyətindədir, {expected} gözlənilirdi.");
        }
    }
}

/// <summary>Sifariş ödənildi → fan-out başlaya bilər (və audit).</summary>
public sealed record CorporateOrderPaid(
    CorporateGiftOrderId OrderId, CorporateId CorporateId, long TotalQepik, CorporatePaymentMethod Method, DateTimeOffset At) : DomainEvent(At);
