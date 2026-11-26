using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;

namespace Qonaqet.Domain.Leaderboard;

/// <summary>Xal dəyərləri (<c>Leaderboard:Points</c>); default-lar CLAUDE.md → "Leaderboard" cədvəlidir.</summary>
public sealed record ScorePoints
{
    /// <summary>Şəxsi hədiyyə: təhvil olunan hər məhsul vahidi.</summary>
    public int GiftItemUnit { get; init; } = 1;

    /// <summary>Şəxsi hədiyyə: vauçerdə təhvil olunan dəyərin hər başlanan <see cref="GiftVoucherStepQepik"/>-i.</summary>
    public int GiftVoucherStep { get; init; } = 1;

    public long GiftVoucherStepQepik { get; init; } = 500;

    /// <summary>Sosial qonaqlıq: donorun (remote, hovuz ianəsi, kampaniya) təhvil olunan hər claim-i.</summary>
    public int DonorClaim { get; init; } = 2;

    /// <summary>Biznes: öz <c>BusinessDaily</c> və <c>Suspended</c> claim-i.</summary>
    public int BusinessClaim { get; init; } = 2;

    /// <summary>Şirkət: təhvil olunan hər korporativ hədiyyə (hissəli təhvildə də bir dəfə).</summary>
    public int CorporateGift { get; init; } = 1;

    /// <summary>Şirkət: CSR kampaniyasından maliyyələşən hər claim.</summary>
    public int CorporateClaim { get; init; } = 2;
}

/// <summary>Anti-gaming limitləri (<c>Leaderboard:Limits</c>).</summary>
public sealed record ScoreLimits
{
    /// <summary>Eyni göndərən→alıcı cütü üçün Bakı ayında maksimum xal.</summary>
    public int PairPerMonth { get; init; } = 10;

    /// <summary>İstifadəçi başına Bakı günündə maksimum xal; artığı <c>points = 0</c> ilə yazılır.</summary>
    public int UserPerDay { get; init; } = 300;

    /// <summary>Limitlərdən sonra qalan xal: qazanılan, cütün bu ayki və istifadəçinin bugünkü qalığından ən kiçiyi (mənfi olmur).</summary>
    public int Allow(int earned, int? pairThisMonth, int userToday)
    {
        var allowed = Math.Min(earned, UserPerDay - userToday);
        if (pairThisMonth is { } pair)
        {
            allowed = Math.Min(allowed, PairPerMonth - pair);
        }

        return Math.Max(0, allowed);
    }
}

/// <summary>
/// Hədiyyə təhvili (<see cref="GiftRedeemed"/>-in xal üçün lazım olan hissəsi). <see cref="RecipientUserId"/> yalnız email-i təsdiqlənmiş
/// hesab olduqda doludur (email hesaba uyğun gəlib və ya "Hesabıma əlavə et").
/// </summary>
public sealed record RedeemedGift(
    Guid GiftId, Guid RedemptionId, GiftKind Kind, long ValueQepik, int Units, Guid? SenderUserId, Guid? CorporateId, Guid? RecipientUserId);

/// <summary>Claim təhvili (<see cref="ClaimRedeemed"/>-in xal üçün lazım olan hissəsi).</summary>
public sealed record RedeemedClaim(Guid ClaimId, Guid BusinessId, OfferType? OfferType, long? AmountQepik, Guid? FunderUserId, Guid? FunderCorporateId);

/// <summary>
/// Xalın kimə və nə qədər yazılacağı (təmiz qayda; gündəlik və cüt limitləri <see cref="ScoreLimits"/> ilə ayrıca tətbiq olunur).
/// <see cref="RecipientUserId"/> yalnız göndərən→alıcı cüt limiti üçündür.
/// </summary>
public sealed record ScoreAward(
    ScoreSubjectType SubjectType, Guid SubjectId, string SourceType, Guid SourceId, int Points, long Value, Guid? RecipientUserId = null);

/// <summary>
/// Təhvil hadisəsindən xal: yalnız real qonaqlıq sayılır, ona görə xal alışda yox, təhvildə verilir.
/// <list type="bullet">
/// <item>Hədiyyə: göndərənə vahid başına (məhsul) və ya hər başlanan 5 AZN üçün (vauçer) xal; şirkətə hədiyyə başına bir dəfə.
/// Göndərən özü alıcıdırsa və ya alıcının təsdiqlənmiş hesabı yoxdursa (yalnız link), göndərən xal almır; şirkət də yalnız alıcının
/// təsdiqlənmiş hesabı olduqda xal alır.</item>
/// <item>Claim: <c>BusinessDaily</c>/<c>Suspended</c> → biznesə; remote/hovuz → donora; CSR kampaniyası → şirkətə.</item>
/// </list>
/// </summary>
public sealed class ScoringPolicy(ScorePoints points)
{
    public const string GiftRedemptionSource = "GiftRedemption";

    public const string GiftSource = "Gift";

    public const string ClaimSource = "Claim";

    public IReadOnlyList<ScoreAward> ForGift(RedeemedGift redeemed)
    {
        ArgumentNullException.ThrowIfNull(redeemed);
        var awards = new List<ScoreAward>();
        if (redeemed is { SenderUserId: { } senderId, RecipientUserId: { } recipientId } && senderId != recipientId)
        {
            var earned = redeemed.Kind == GiftKind.Items
                ? redeemed.Units * points.GiftItemUnit
                : (int)((redeemed.ValueQepik + points.GiftVoucherStepQepik - 1) / points.GiftVoucherStepQepik) * points.GiftVoucherStep;
            awards.Add(new ScoreAward(ScoreSubjectType.User, senderId, GiftRedemptionSource, redeemed.RedemptionId, earned, redeemed.ValueQepik, recipientId));
        }

        if (redeemed is { CorporateId: { } corporateId, RecipientUserId: not null })
        {
            awards.Add(new ScoreAward(ScoreSubjectType.Corporate, corporateId, GiftSource, redeemed.GiftId, points.CorporateGift, redeemed.ValueQepik));
        }

        return awards;
    }

    public IReadOnlyList<ScoreAward> ForClaim(RedeemedClaim redeemed)
    {
        ArgumentNullException.ThrowIfNull(redeemed);
        var claimId = redeemed.ClaimId;
        var value = redeemed.AmountQepik ?? 0;
        var awards = new List<ScoreAward>();
        if (redeemed.OfferType is OfferType.BusinessDaily or OfferType.Suspended)
        {
            awards.Add(new ScoreAward(ScoreSubjectType.Business, redeemed.BusinessId, ClaimSource, claimId, points.BusinessClaim, value));
        }

        if (redeemed.FunderUserId is { } donorId)
        {
            awards.Add(new ScoreAward(ScoreSubjectType.User, donorId, ClaimSource, claimId, points.DonorClaim, value));
        }

        if (redeemed.FunderCorporateId is { } corporateId)
        {
            awards.Add(new ScoreAward(ScoreSubjectType.Corporate, corporateId, ClaimSource, claimId, points.CorporateClaim, value));
        }

        return awards;
    }
}
