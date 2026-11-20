using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Hədiyyələrin sabit xəta kodları.</summary>
public static class GiftErrors
{
    public static readonly Error NotFound = Error.NotFound("gift_not_found", "Hədiyyə tapılmadı.");

    public static readonly Error ScopeInvalid = Error.Validation("gift_scope_invalid", "Şəbəkə hədiyyəsi yalnız vauçer ola bilər.");

    public static readonly Error ItemsInvalid =
        Error.Validation("gift_items_invalid", "Bir biznesdən 1–10 məhsul seçin (eyni məhsul bir sətirdə).");

    public static readonly Error ItemUnavailable =
        Error.Validation("gift_item_unavailable", "Seçilmiş məhsul hədiyyə üçün mövcud deyil.");

    public static readonly Error AmountOutOfRange = Error.Validation("gift_amount_out_of_range", "Vauçerin məbləği icazə verilən aralıqda deyil.");

    public static readonly Error BusinessUnavailable = Error.Conflict("gift_business_unavailable", "Bu biznes hazırda hədiyyə qəbul etmir.");

    public static readonly Error RecipientRequired = Error.Validation("gift_recipient_required", "Alıcının adını yazın.");

    public static readonly Error SelfGift = Error.Validation("gift_self_not_allowed", "Hədiyyəni özünüzə göndərə bilməzsiniz.");

    public static readonly Error DeliverAtOutOfRange = Error.Validation("gift_deliver_at_out_of_range", "Çatdırılma ən çox 90 gün irəli planlana bilər.");

    public static readonly Error ExpiryPoolUnavailable = Error.Conflict("gift_expiry_pool_unavailable", "Bağışlama üçün hovuz tapılmadı.");

    public static readonly Error Expired = Error.Conflict("gift_expired", "Hədiyyənin müddəti bitib.");

    public static readonly Error InsufficientBalance = Error.Conflict("gift_insufficient_balance", "Hədiyyədə bu qədər qalıq yoxdur.");

    public static readonly Error Changed = Error.Conflict("gift_changed", "Hədiyyə elə indi dəyişdi. Yenidən yoxlayın.");

    public static readonly Error NotRedeemable = Error.Conflict("gift_not_redeemable", "Bu hədiyyə hazırda təhvil verilə bilməz.");

    public static readonly Error NetworkNotAccepted =
        Error.Conflict("network_voucher_not_accepted", "Bu məkan QONAQET şəbəkə vauçerlərini qəbul etmir.");

    public static readonly Error SelectionInvalid = Error.Validation("gift_selection_invalid", "Təhvil üçün məhsul sətirlərini və ya məbləği düzgün seçin.");

    public static readonly Error NotCancellable = Error.Conflict("gift_not_cancellable", "Yalnız hələ çatdırılmamış hədiyyə ləğv edilə bilər.");

    public static readonly Error LinkAlreadyRevealed =
        Error.Conflict("gift_link_already_revealed", "Link artıq verilib. Yeni link üçün linki yeniləyin.");

    public static readonly Error LinkUnavailable = Error.Conflict("gift_link_unavailable", "Bu hədiyyənin linki hazırda verilə bilməz.");

    public static readonly Error AlreadyClaimed = Error.Conflict("gift_already_claimed", "Bu hədiyyə artıq başqa hesaba əlavə olunub.");
}
