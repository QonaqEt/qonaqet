namespace Qonaqet.Domain.Gifts;

/// <summary>Nə hədiyyə edilir: məhsullar və ya pul vauçeri.</summary>
public enum GiftKind
{
    Items,
    Voucher,
}

/// <summary>Harada işlənir: bir biznesdə və ya şəbəkədə (<c>acceptsNetworkVouchers</c> olan istənilən biznesdə; yalnız vauçer).</summary>
public enum GiftScope
{
    Business,
    Network,
}

/// <summary>Hədiyyə həmişə biznesdə götürülür (QR/kod). <c>Delivery</c> gələcəkdə əlavə oluna bilər.</summary>
public enum FulfillmentType
{
    Pickup,
}

/// <summary>Göndərənin adı alıcıya göstərilirmi.</summary>
public enum SenderDisplay
{
    Named,
    Anonymous,
}

/// <summary>Hədiyyə teması: hər birinin öz illüstrasiyası və vurğu rəngi var (dizayn token-lərində).</summary>
public enum GiftTheme
{
    Birthday,
    Love,
    Thanks,
    Family,
    Congrats,
    JustBecause,
    Corporate,
}

public enum GiftStatus
{
    /// <summary>Yaradılıb, ödəniş gözlənilir.</summary>
    AwaitingPayment,

    /// <summary>Ödənilib, <c>deliverAt</c> gözlənilir.</summary>
    Scheduled,

    /// <summary>Çatdırılıb (email göndərilib və ya link göndərənə verilib).</summary>
    Delivered,

    /// <summary>Alıcı linki açıb.</summary>
    Opened,

    PartiallyRedeemed,

    Redeemed,

    /// <summary>Müddəti bitib; istifadə olunmamış dəyər <see cref="GiftExpiryAction"/>-a uyğun refund olunur və ya hovuza bağışlanır.</summary>
    Expired,

    /// <summary>Müddət bitməsində qalıq ödəyiciyə qaytarılıb.</summary>
    Refunded,

    /// <summary>Göndərən ləğv edib (tam refund) və ya ödəniş baş tutmayıb.</summary>
    Cancelled,
}

/// <summary>Müddət bitəndə istifadə olunmamış dəyərin taleyi.</summary>
public enum GiftExpiryAction
{
    /// <summary>Qalıq ödəyiciyə qismən refund olunur (şəxsi kart hədiyyələrində default).</summary>
    RefundToPayer,

    /// <summary>Qalıq hovuza bağışlanır (<c>GiftExpiryDonation</c>); korporativ bank köçürməsi sifarişlərində default.</summary>
    DonateToPool,
}

/// <summary>Təhvilin niyə qəbul edilmədiyi (tətbiq qatı sabit xəta koduna çevirir).</summary>
public enum GiftRedeemRejection
{
    NotRedeemable,
    Expired,
    AlreadyRedeemed,
    WrongBusiness,
    InvalidSelection,
    InsufficientBalance,
}
