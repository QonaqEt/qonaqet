namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçinin söndürə bildiyi bildiriş növləri. Növlər ümumidir (bizneslər, hədiyyələr, kampaniyalar), konkret
/// kateqoriyaya bağlı deyil. Giriş kodu (OTP) bildiriş növü deyil — həmişə göndərilir.
/// </summary>
public enum NotificationType
{
    /// <summary>Sizə hədiyyə gəldi.</summary>
    GiftReceived,

    /// <summary>Göndərdiyiniz hədiyyə çatdırıldı / baxıldı / istifadə olundu.</summary>
    GiftStatus,

    /// <summary>Hədiyyənin müddəti bitir.</summary>
    GiftExpiring,

    /// <summary>Ismarlama: claim-lər, sizə ısmarlandı.</summary>
    SocialTreats,

    /// <summary>Status (kateqoriya) təsdiqi və yeniləmə xatırlatmaları.</summary>
    Verification,

    /// <summary>Reytinq xalları, səviyyə və nişanlar.</summary>
    Leaderboard,

    /// <summary>Kampaniyalar və QONAQET Plus təklifləri.</summary>
    Campaigns,
}

public enum NotificationChannel
{
    Push,
    Email,
}

/// <summary>Söndürülmüş (növ, kanal) cütü. Default olaraq hər şey açıqdır; yalnız söndürülənlər saxlanılır.</summary>
public sealed record NotificationOptOut(NotificationType Type, NotificationChannel Channel);
