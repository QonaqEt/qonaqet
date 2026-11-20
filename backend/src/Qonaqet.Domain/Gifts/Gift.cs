using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Gifts;

public readonly record struct GiftId(Guid Value) : IStronglyTypedId<GiftId>
{
    public static GiftId From(Guid value) => new(value);
}

/// <summary>Hədiyyə qaydaları (sabitlər). Qiymət həmişə serverdə hesablanır.</summary>
public static class GiftRules
{
    /// <summary>Məhsul hədiyyəsində ən çox sətir.</summary>
    public const int MaxLines = 10;

    /// <summary>Məhsul hədiyyəsində ümumi vahid sayı 1–10.</summary>
    public const int MaxItemUnits = 10;

    /// <summary>Şəbəkə vauçeri 2–200 AZN.</summary>
    public const long NetworkMinQepik = 200;

    public const long NetworkMaxQepik = 20_000;

    public const int MessageMaxLength = 500;

    public const int RecipientNameMaxLength = 100;

    public const int SenderNameMaxLength = 100;

    /// <summary><c>deliverAt</c> ən çox 90 gün irəli.</summary>
    public static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(90);

    /// <summary><c>Gifts:ValidityDays</c> default-u.</summary>
    public const int DefaultValidityDays = 60;

    /// <summary>Alıcıya müddətin bitməsi barədə xatırlatma neçə gün qabaq.</summary>
    public static readonly TimeSpan ExpiryReminderWindow = TimeSpan.FromDays(7);

    /// <summary>Anonim göndərənin alıcıya görünən adı.</summary>
    public const string AnonymousSenderName = "Anonim dost";

    public static bool IsValidNetworkAmount(long amountQepik) => amountQepik is >= NetworkMinQepik and <= NetworkMaxQepik;
}

/// <summary>Hədiyyədəki məhsul sətri (snapshot: ad və qiymət yaradılan anda dondurulur).</summary>
public sealed class GiftItem
{
    private GiftItem()
    {
    }

    public static GiftItem Create(CatalogItemId catalogItemId, string title, long unitPriceQepik, int qty) => new()
    {
        CatalogItemId = catalogItemId,
        Title = Guard.NotNullOrWhiteSpace(title),
        UnitPriceQepik = Guard.Positive(unitPriceQepik),
        Qty = Guard.InRange(qty, 1, GiftRules.MaxItemUnits),
    };

    public CatalogItemId CatalogItemId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public long UnitPriceQepik { get; private set; }

    public int Qty { get; private set; }

    public int RedeemedQty { get; private set; }

    public int RemainingQty => Qty - RedeemedQty;

    internal void Redeem(int qty)
    {
        if (qty <= 0 || qty > RemainingQty)
        {
            throw new InvalidOperationException("Sətir üzrə miqdar qalığı aşır.");
        }

        RedeemedQty += qty;
    }
}

/// <summary>Təhvildə seçilən sətir (məhsul hədiyyəsi).</summary>
public sealed record GiftLine(CatalogItemId CatalogItemId, int Qty);

/// <summary>Hədiyyə olunan şey (serverdə hesablanmış): növ, əhatə, biznes, sətirlər və məbləğ.</summary>
public sealed class GiftContent
{
    private GiftContent(GiftKind kind, GiftScope scope, BusinessId? businessId, IReadOnlyList<GiftItem> items, long amountQepik)
    {
        Kind = kind;
        Scope = scope;
        BusinessId = businessId;
        Items = items;
        AmountQepik = Guard.Positive(amountQepik);
    }

    public GiftKind Kind { get; }

    public GiftScope Scope { get; }

    public BusinessId? BusinessId { get; }

    public IReadOnlyList<GiftItem> Items { get; }

    public long AmountQepik { get; }

    /// <summary>Bir biznesin 1–10 vahid məhsulu; məbləğ = Σ qiymət × say.</summary>
    public static GiftContent ForItems(BusinessId businessId, IReadOnlyList<GiftItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is 0 or > GiftRules.MaxLines || items.Sum(i => i.Qty) > GiftRules.MaxItemUnits)
        {
            throw new ArgumentException("Məhsul hədiyyəsi 1–10 vahid olmalıdır.", nameof(items));
        }

        if (items.Select(i => i.CatalogItemId).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("Eyni məhsul iki sətirdə ola bilməz.", nameof(items));
        }

        return new GiftContent(GiftKind.Items, GiftScope.Business, businessId, [.. items], items.Sum(i => i.UnitPriceQepik * i.Qty));
    }

    /// <summary>Biznes vauçeri: məbləğ biznesin <c>voucherMinQepik</c>–<c>voucherMaxQepik</c> aralığındadır (çağıran yoxlayır).</summary>
    public static GiftContent ForBusinessVoucher(BusinessId businessId, long amountQepik) =>
        new(GiftKind.Voucher, GiftScope.Business, businessId, [], amountQepik);

    /// <summary>Şəbəkə vauçeri: 2–200 AZN, <c>acceptsNetworkVouchers</c> olan istənilən biznesdə.</summary>
    public static GiftContent ForNetworkVoucher(long amountQepik)
    {
        if (!GiftRules.IsValidNetworkAmount(amountQepik))
        {
            throw new ArgumentOutOfRangeException(nameof(amountQepik), "Şəbəkə vauçeri 2–200 AZN olmalıdır.");
        }

        return new GiftContent(GiftKind.Voucher, GiftScope.Network, null, [], amountQepik);
    }
}

/// <summary>Göndərən: şəxs (<paramref name="UserId"/>) və ya korporativ sifariş; <paramref name="Name"/> snapshot-dır (korporativdə şirkətin adı).</summary>
public sealed record GiftSender(Guid? UserId, Guid? CorporateOrderId, SenderDisplay Display, string Name);

/// <summary>Alıcı: ad məcburidir; email yoxdursa göndərən linki özü paylaşır.</summary>
public sealed record GiftRecipient(string Name, string? Email);

/// <summary>Link token-inin saxlanılan formaları: hash (axtarış üçün) və müvəqqəti şifrəli nüsxə (email/göndərən üçün, sonra silinir).</summary>
public sealed record GiftLinkSecret(string TokenHash, string Sealed);

/// <summary>Müddət bitməsində nə edilir.</summary>
public sealed record GiftExpiryPlan(GiftExpiryAction Action, PoolId? PoolId);

/// <summary>
/// Şəxsə ünvanlanmış hədiyyə (<c>gifts</c>). Həmişə biznesdə götürülür (QR və ya qısa kod). Pul qəpiklə saxlanılır; vauçer qismən təhvil
/// verilə bilər. Link token-i bearer-dir: yalnız <see cref="LinkTokenHash"/> axtarış üçün saxlanılır, açıq token yalnız çatdırılma
/// emaili və göndərənə bir dəfəlik göstərilməsi üçün qısa müddət şifrəli (<see cref="LinkTokenSealed"/>) qalır.
/// Hər dəyişiklik <see cref="Version"/>-u artırır: təhvil optimistik versiya şərti ilə yazılır.
/// </summary>
public sealed class Gift : AggregateRoot<GiftId>
{
    private List<GiftItem> _items = [];

    private Gift(GiftId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Gift()
    {
    }

    public GiftKind Kind { get; private set; }

    public GiftScope Scope { get; private set; }

    /// <summary><see cref="GiftScope.Business"/> əhatəsində biznes; şəbəkə vauçerində <c>null</c>.</summary>
    public BusinessId? BusinessId { get; private set; }

    public IReadOnlyList<GiftItem> Items
    {
        get => _items;
        private set => _items = [.. value];
    }

    /// <summary>Hədiyyənin ümumi dəyəri (məhsullarda Σ qiymət × say).</summary>
    public long AmountQepik { get; private set; }

    /// <summary>Hələ təhvil verilməmiş dəyər.</summary>
    public long RemainingQepik { get; private set; }

    public FulfillmentType FulfillmentType { get; private set; } = FulfillmentType.Pickup;

    public Guid? SenderUserId { get; private set; }

    public Guid? CorporateOrderId { get; private set; }

    public SenderDisplay SenderDisplay { get; private set; }

    /// <summary>Göndərənin adı (snapshot); anonim hədiyyədə alıcıya göstərilmir.</summary>
    public string SenderName { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string? RecipientEmail { get; private set; }

    /// <summary>Email hesaba uyğun gələndə və ya alıcı linki "Hesabıma əlavə et" ilə götürəndə təyin olunur.</summary>
    public Guid? RecipientUserId { get; private set; }

    public GiftTheme Theme { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset DeliverAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public GiftStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? LinkTokenHash { get; private set; }

    /// <summary>Açıq token-in şifrəli nüsxəsi: yalnız çatdırılma emaili və ya göndərənə ilk göstərilmə gözlənilərkən saxlanılır.</summary>
    public string? LinkTokenSealed { get; private set; }

    /// <summary>Göndərən linki bir dəfə götürüb (və ya yenidən verib).</summary>
    public DateTimeOffset? LinkRevealedAt { get; private set; }

    /// <summary>Qısa kodun hash-i (kodun özü ID-dən törədilir, saxlanılmır).</summary>
    public string? ShortCodeHash { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? OpenedAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public long RedeemedQepik { get; private set; }

    public GiftExpiryAction ExpiryAction { get; private set; }

    public PoolId? ExpiryPoolId { get; private set; }

    public DateTimeOffset? ExpiryReminderSentAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Ödəyiciyə qaytarılmış məbləğ (ləğv və ya müddət bitməsi).</summary>
    public long RefundedQepik { get; private set; }

    /// <summary>Qaytarılmalı, amma hələ provayderdə tamamlanmamış məbləğ (ləğvdə tam, müddət bitməsində qalıq); job təkrar cəhd edir.</summary>
    public long RefundDueQepik { get; private set; }

    public GiftRefundReason? RefundReason { get; private set; }

    /// <summary>Müddət bitməsində hovuza bağışlanan məbləğ.</summary>
    public long DonatedQepik { get; private set; }

    /// <summary>Korporativ sifarişdə alıcının sıra nömrəsi (CSV-dəki yeri): (<c>corporateOrderId</c>, bu) unikaldır → fan-out idempotentdir.</summary>
    public int? CorporateRecipientIndex { get; private set; }

    /// <summary>Optimistik konkurensiya: hər dəyişiklikdə artır.</summary>
    public long Version { get; private set; }

    /// <summary>Alıcıya görünən göndərən adı.</summary>
    public string SenderDisplayName => SenderDisplay == SenderDisplay.Anonymous ? GiftRules.AnonymousSenderName : SenderName;

    /// <summary>Alıcının əlində olan (çatdırılmış, tam istifadə olunmamış) hədiyyə.</summary>
    public bool IsLive => Status is GiftStatus.Delivered or GiftStatus.Opened or GiftStatus.PartiallyRedeemed;

    /// <summary>Bu anda təhvil verilə bilərmi.</summary>
    public bool IsRedeemableAt(DateTimeOffset now) => IsLive && now < ExpiresAt && RemainingQepik > 0;

    /// <summary>Ödənilib, amma hələ yekunlaşmayıb (çatdırılma gözlənilir və ya alıcının əlindədir).</summary>
    public bool IsActive => Status == GiftStatus.Scheduled || IsLive;

    public int RemainingUnits => _items.Sum(i => i.RemainingQty);

    public static Gift Create(
        GiftContent content, GiftSender sender, GiftRecipient recipient, GiftTheme theme, string? message, DateTimeOffset deliverAt,
        int validityDays, GiftExpiryPlan expiry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(expiry);
        Guard.Utc(now);
        Guard.Utc(deliverAt);
        if (sender.UserId is null && sender.CorporateOrderId is null)
        {
            throw new ArgumentException("Göndərən şəxs və ya korporativ sifariş olmalıdır.", nameof(sender));
        }

        if (!Enum.IsDefined(theme) || !Enum.IsDefined(sender.Display) || !Enum.IsDefined(expiry.Action))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        if (deliverAt > now + GiftRules.MaxScheduleAhead)
        {
            throw new ArgumentOutOfRangeException(nameof(deliverAt), "Çatdırılma ən çox 90 gün irəli planlana bilər.");
        }

        if (message is { Length: > GiftRules.MessageMaxLength })
        {
            throw new ArgumentException("Mesaj ən çox 500 simvol ola bilər.", nameof(message));
        }

        if (expiry.Action == GiftExpiryAction.DonateToPool && expiry.PoolId is null)
        {
            throw new ArgumentException("Hovuza bağışlama üçün hovuz lazımdır.", nameof(expiry));
        }

        var effectiveDeliverAt = deliverAt < now ? now : deliverAt;
        return new Gift(StronglyTypedId.New<GiftId>(), now, sender.UserId)
        {
            Kind = content.Kind,
            Scope = content.Scope,
            BusinessId = content.BusinessId,
            Items = content.Items,
            AmountQepik = content.AmountQepik,
            RemainingQepik = content.AmountQepik,
            FulfillmentType = FulfillmentType.Pickup,
            SenderUserId = sender.UserId,
            CorporateOrderId = sender.CorporateOrderId,
            SenderDisplay = sender.Display,
            SenderName = Guard.NotNullOrWhiteSpace(sender.Name).Trim(),
            RecipientName = Guard.NotNullOrWhiteSpace(recipient.Name).Trim(),
            RecipientEmail = string.IsNullOrWhiteSpace(recipient.Email) ? null : recipient.Email.Trim().ToLowerInvariant(),
            Theme = theme,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            DeliverAt = effectiveDeliverAt,
            ExpiresAt = effectiveDeliverAt.AddDays(Guard.InRange(validityDays, 1, 3650)),
            Status = GiftStatus.AwaitingPayment,
            ExpiryAction = expiry.Action,
            ExpiryPoolId = expiry.PoolId,
        };
    }

    /// <summary>
    /// Korporativ sifarişin bir alıcısı üçün hədiyyə (fan-out): göndərən şirkətdir (adı snapshot, tema <c>Corporate</c>), sifariş artıq ödənilib —
    /// hədiyyə dərhal <c>Scheduled</c>-dir və link token-i bağlanır. Linki götürəcək şəxs göndərən yoxdur, ona görə şifrəli nüsxə yalnız
    /// çatdırılma emailinə qədər saxlanılır.
    /// </summary>
    public static Gift CreateForCorporateOrder(
        GiftContent content, Guid corporateOrderId, string corporateName, Guid? paymentId, GiftRecipient recipient, int recipientIndex, string? message,
        DateTimeOffset deliverAt, int validityDays, GiftExpiryPlan expiry, GiftLinkSecret link, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(link);
        var gift = Create(
            content, new GiftSender(null, Guard.NotEmpty(corporateOrderId), SenderDisplay.Named, corporateName), recipient, GiftTheme.Corporate, message,
            deliverAt < now ? now : deliverAt, validityDays, expiry, now);
        gift.PaymentId = paymentId;
        gift.CorporateRecipientIndex = Guard.InRange(recipientIndex, 0, int.MaxValue);
        gift.Status = GiftStatus.Scheduled;
        gift.PaidAt = now;
        gift.SetLink(link);
        gift.LinkRevealedAt = now;
        gift.TrimSealedToken();
        return gift;
    }

    public void AttachPayment(Guid paymentId)
    {
        if (PaymentId is not null)
        {
            throw new InvalidOperationException("Ödəniş artıq bağlanıb.");
        }

        PaymentId = Guard.NotEmpty(paymentId);
    }

    /// <summary>Qısa kodun hash-ini bağlayır (bir dəfə; kod hədiyyə üçün sabitdir).</summary>
    public void AttachShortCode(string shortCodeHash)
    {
        if (ShortCodeHash is not null)
        {
            throw new InvalidOperationException("Qısa kod artıq bağlanıb.");
        }

        ShortCodeHash = Guard.NotNullOrWhiteSpace(shortCodeHash);
    }

    /// <summary>Ödəniş təsdiqləndi: <c>AwaitingPayment</c> → <c>Scheduled</c>, link token-i bağlanır. Təkrar çağırış <c>false</c>.</summary>
    public bool ConfirmPaid(GiftLinkSecret link, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (Status != GiftStatus.AwaitingPayment)
        {
            return false;
        }

        Status = GiftStatus.Scheduled;
        PaidAt = Guard.Utc(at);
        SetLink(link);
        Touch(at, null);
        return true;
    }

    /// <summary>Ödəniş baş tutmadı: <c>AwaitingPayment</c> → <c>Cancelled</c> (pul alınmayıb, refund yoxdur).</summary>
    public bool CancelUnpaid(DateTimeOffset at)
    {
        if (Status != GiftStatus.AwaitingPayment)
        {
            return false;
        }

        Status = GiftStatus.Cancelled;
        CancelledAt = Guard.Utc(at);
        Touch(at, null);
        return true;
    }

    /// <summary>Göndərən linki bir dəfə götürür (ödənişdən sonra). Artıq götürülübsə <c>false</c> — yeni link üçün <see cref="ReissueLink"/>.</summary>
    public bool RevealLink(DateTimeOffset at)
    {
        if (!IsActive || LinkRevealedAt is not null || LinkTokenSealed is null)
        {
            return false;
        }

        LinkRevealedAt = Guard.Utc(at);
        TrimSealedToken();
        Touch(at, SenderUserId);
        return true;
    }

    /// <summary>Yeni token: köhnə hash əvəz olunur (köhnə link dərhal işləmir). Yeni link cavabda göndərənə verilir.</summary>
    public void ReissueLink(GiftLinkSecret link, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!IsActive)
        {
            throw new InvalidOperationException($"Hədiyyə {Status} vəziyyətindədir, link yenilənə bilməz.");
        }

        SetLink(link);
        LinkRevealedAt = Guard.Utc(at);
        TrimSealedToken();
        Touch(at, by);
    }

    /// <summary>Çatdırılma (job): <c>Scheduled</c> → <c>Delivered</c>; email hesaba uyğundursa alıcı bağlanır.</summary>
    public void Deliver(Guid? recipientUserId, DateTimeOffset at)
    {
        if (Status != GiftStatus.Scheduled)
        {
            throw new InvalidOperationException($"Hədiyyə {Status} vəziyyətindədir, çatdırıla bilməz.");
        }

        Status = GiftStatus.Delivered;
        DeliveredAt = Guard.Utc(at);
        RecipientUserId ??= recipientUserId;
        TrimSealedToken();
        Touch(at, null);
        Raise(new GiftDelivered(Id, at));
    }

    /// <summary>İlk açılış: <c>Delivered</c> → <c>Opened</c>. Sonrakı açılışlar heç nə dəyişmir (<c>false</c>).</summary>
    public bool Open(DateTimeOffset at)
    {
        if (Status != GiftStatus.Delivered)
        {
            return false;
        }

        Status = GiftStatus.Opened;
        OpenedAt = Guard.Utc(at);
        Touch(at, null);
        Raise(new GiftOpened(Id, at));
        return true;
    }

    /// <summary>
    /// "Hesabıma əlavə et": alıcı hesabı bağlanır (link işləməyə davam edir). Eyni istifadəçi üçün təkrar çağırış <c>true</c>; başqa
    /// istifadəçiyə artıq bağlanıbsa <c>false</c>.
    /// </summary>
    public bool ClaimTo(Guid userId, DateTimeOffset at)
    {
        Guard.NotEmpty(userId);
        if (RecipientUserId is { } existing)
        {
            return existing == userId;
        }

        if (Status is GiftStatus.AwaitingPayment or GiftStatus.Scheduled or GiftStatus.Cancelled)
        {
            throw new InvalidOperationException($"Hədiyyə {Status} vəziyyətindədir.");
        }

        RecipientUserId = userId;
        ClaimedAt = Guard.Utc(at);
        if (Status == GiftStatus.Delivered)
        {
            Status = GiftStatus.Opened;
            OpenedAt = at;
        }

        Touch(at, userId);
        return true;
    }

    /// <summary>Təhvil mümkündürmü; mümkün deyilsə səbəb.</summary>
    public GiftRedeemRejection? CheckRedeem(
        BusinessId businessId, bool businessAcceptsNetwork, IReadOnlyList<GiftLine>? lines, long? amountQepik, DateTimeOffset now)
    {
        if (Status == GiftStatus.Redeemed)
        {
            return GiftRedeemRejection.AlreadyRedeemed;
        }

        if (Status is GiftStatus.Expired or GiftStatus.Refunded || (IsLive && now >= ExpiresAt))
        {
            return GiftRedeemRejection.Expired;
        }

        if (!IsLive)
        {
            return GiftRedeemRejection.NotRedeemable;
        }

        if (Scope == GiftScope.Business ? BusinessId != businessId : !businessAcceptsNetwork)
        {
            return GiftRedeemRejection.WrongBusiness;
        }

        if (Kind == GiftKind.Items)
        {
            if (lines is null || lines.Count == 0 || amountQepik is not null || lines.Any(l => l.Qty <= 0)
                || lines.Select(l => l.CatalogItemId).Distinct().Count() != lines.Count)
            {
                return GiftRedeemRejection.InvalidSelection;
            }

            foreach (var line in lines)
            {
                var item = _items.FirstOrDefault(i => i.CatalogItemId == line.CatalogItemId);
                if (item is null)
                {
                    return GiftRedeemRejection.InvalidSelection;
                }

                if (line.Qty > item.RemainingQty)
                {
                    return GiftRedeemRejection.InsufficientBalance;
                }
            }

            return null;
        }

        if (amountQepik is not { } amount || amount <= 0 || lines is { Count: > 0 })
        {
            return GiftRedeemRejection.InvalidSelection;
        }

        return amount > RemainingQepik ? GiftRedeemRejection.InsufficientBalance : null;
    }

    /// <summary>
    /// Təhvil (qismən ola bilər): qalıq azalır, status <c>PartiallyRedeemed</c>/<c>Redeemed</c> olur, <see cref="GiftRedeemed"/> qaldırılır.
    /// Qayıdan <see cref="GiftRedemption"/> eyni tranzaksiyada yazılmalıdır.
    /// </summary>
    public GiftRedemption Redeem(
        BusinessId businessId, bool businessAcceptsNetwork, IReadOnlyList<GiftLine>? lines, long? amountQepik, Guid staffUserId, DateTimeOffset now)
    {
        if (CheckRedeem(businessId, businessAcceptsNetwork, lines, amountQepik, now) is { } rejection)
        {
            throw new InvalidOperationException($"Təhvil mümkün deyil: {rejection}.");
        }

        long value;
        IReadOnlyList<GiftLine> redeemedLines = [];
        var units = 0;
        if (Kind == GiftKind.Items)
        {
            value = 0;
            foreach (var line in lines!)
            {
                var item = _items.First(i => i.CatalogItemId == line.CatalogItemId);
                item.Redeem(line.Qty);
                value += item.UnitPriceQepik * line.Qty;
                units += line.Qty;
            }

            redeemedLines = [.. lines];
        }
        else
        {
            value = amountQepik!.Value;
        }

        RemainingQepik -= value;
        RedeemedQepik += value;
        Status = RemainingQepik == 0 ? GiftStatus.Redeemed : GiftStatus.PartiallyRedeemed;
        Touch(now, staffUserId);

        var redemption = GiftRedemption.Record(Id, businessId, Guard.NotEmpty(staffUserId), redeemedLines, Kind == GiftKind.Voucher ? value : null, value, now);
        Raise(new GiftRedeemed(
            Id, redemption.Id, businessId, Kind, Scope, value, units, SenderUserId, CorporateOrderId, RecipientUserId, RecipientEmail is not null, now));
        return redemption;
    }

    /// <summary>Göndərənin ləğvi: yalnız <c>Scheduled</c>; tam refund tələb olunur.</summary>
    public void Cancel(DateTimeOffset at, Guid? by)
    {
        if (Status != GiftStatus.Scheduled)
        {
            throw new InvalidOperationException($"Hədiyyə {Status} vəziyyətindədir, ləğv edilə bilməz.");
        }

        Status = GiftStatus.Cancelled;
        CancelledAt = Guard.Utc(at);
        TrimSealedToken();
        Touch(at, by);
        if (PaymentId is { } paymentId)
        {
            RefundDueQepik = AmountQepik;
            RefundReason = GiftRefundReason.Cancelled;
            Raise(new GiftRefundRequested(Id, paymentId, AmountQepik, GiftRefundReason.Cancelled, at));
        }
    }

    /// <summary>Müddət bitdi: <c>Expired</c>; qalıq <see cref="ExpiryAction"/>-a uyğun refund tələb olunur və ya hovuza bağışlanır.</summary>
    public void Expire(DateTimeOffset at)
    {
        if (!(IsLive || Status == GiftStatus.Scheduled) || at < ExpiresAt)
        {
            throw new InvalidOperationException($"Hədiyyə {Status} vəziyyətindədir və ya müddəti bitməyib.");
        }

        Status = GiftStatus.Expired;
        ExpiredAt = Guard.Utc(at);
        TrimSealedToken();
        Touch(at, null);
        if (RemainingQepik <= 0)
        {
            return;
        }

        if (ExpiryAction == GiftExpiryAction.RefundToPayer && PaymentId is { } paymentId)
        {
            RefundDueQepik = RemainingQepik;
            RefundReason = GiftRefundReason.Expired;
            Raise(new GiftRefundRequested(Id, paymentId, RemainingQepik, GiftRefundReason.Expired, at));
        }
        else if (ExpiryPoolId is { } poolId)
        {
            DonatedQepik = RemainingQepik;
            Raise(new GiftExpiryDonated(Id, poolId, RemainingQepik, at));
        }
    }

    /// <summary>Refund uğurla tamamlandı. Müddəti bitmiş hədiyyə <c>Refunded</c> olur; ləğv olunmuş <c>Cancelled</c> qalır. Təkrar çağırış <c>false</c>.</summary>
    public bool MarkRefunded(long amountQepik, DateTimeOffset at)
    {
        Guard.Positive(amountQepik);
        if (Status is not (GiftStatus.Expired or GiftStatus.Cancelled) || RefundDueQepik <= 0)
        {
            return false;
        }

        RefundedQepik += amountQepik;
        RefundDueQepik = 0;
        if (Status == GiftStatus.Expired)
        {
            Status = GiftStatus.Refunded;
        }

        Touch(at, null);
        return true;
    }

    /// <summary>Müddət xatırlatması göndərildi (bir dəfə).</summary>
    public bool MarkExpiryReminderSent(DateTimeOffset at)
    {
        if (ExpiryReminderSentAt is not null)
        {
            return false;
        }

        ExpiryReminderSentAt = Guard.Utc(at);
        Touch(at, null);
        return true;
    }

    private void SetLink(GiftLinkSecret link)
    {
        LinkTokenHash = Guard.NotNullOrWhiteSpace(link.TokenHash);
        LinkTokenSealed = Guard.NotNullOrWhiteSpace(link.Sealed);
    }

    /// <summary>
    /// Şifrəli token yalnız lazım olduqca saxlanılır: çatdırılma emaili hələ getməyibsə və ya göndərən linki hələ götürməyibsə. Hədiyyə
    /// yekunlaşanda (ləğv, müddət) həmişə silinir.
    /// </summary>
    private void TrimSealedToken()
    {
        var awaitingEmail = Status == GiftStatus.Scheduled && RecipientEmail is not null;
        var awaitingSender = LinkRevealedAt is null && IsActive;
        if (!awaitingEmail && !awaitingSender)
        {
            LinkTokenSealed = null;
        }
    }

    private void Touch(DateTimeOffset at, Guid? by)
    {
        Version++;
        MarkUpdated(at, by);
    }
}

public enum GiftRefundReason
{
    Cancelled,
    Expired,
}

/// <summary>Hədiyyə çatdırıldı (webhook-lar, E13).</summary>
public sealed record GiftDelivered(GiftId GiftId, DateTimeOffset At) : DomainEvent(At);

/// <summary>Alıcı hədiyyəni ilk dəfə açdı.</summary>
public sealed record GiftOpened(GiftId GiftId, DateTimeOffset At) : DomainEvent(At);

/// <summary>
/// Hədiyyə (qismən) təhvil verildi. Ledger: təhvil verən biznesə <c>RedemptionCredit</c> + 7% <c>Commission</c> (hədiyyə həmişə onlayn
/// ödənilib). Xal (E11), webhook (E13) və bildirişlər də bundan qidalanır. Biznes heç vaxt göndərəni/alıcını görmür; ID-lər yalnız daxili
/// consumer-lər üçündür.
/// </summary>
public sealed record GiftRedeemed(
    GiftId GiftId,
    GiftRedemptionId RedemptionId,
    BusinessId BusinessId,
    GiftKind Kind,
    GiftScope Scope,
    long ValueQepik,
    int Units,
    Guid? SenderUserId,
    Guid? CorporateOrderId,
    Guid? RecipientUserId,
    bool HasRecipientEmail,
    DateTimeOffset At) : DomainEvent(At);

/// <summary>Ödəyiciyə refund lazımdır (ləğvdə tam, müddət bitməsində qalıq). Consumer provayderdə refund edir və hədiyyəni işarələyir.</summary>
public sealed record GiftRefundRequested(GiftId GiftId, Guid PaymentId, long AmountQepik, GiftRefundReason Reason, DateTimeOffset At) : DomainEvent(At);

/// <summary>Müddəti bitən hədiyyənin qalığı hovuza bağışlandı → ledger-də hovuza <c>GiftExpiryDonation</c>.</summary>
public sealed record GiftExpiryDonated(GiftId GiftId, PoolId PoolId, long AmountQepik, DateTimeOffset At) : DomainEvent(At);
