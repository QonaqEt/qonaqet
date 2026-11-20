using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Sosial qonaqlıq limitləri (yalnız claim-lərə aiddir; şəxsə ünvanlanmış hədiyyələrin kateqoriya limiti yoxdur):
/// <list type="bullet">
/// <item>eyni anda ən çox bir aktiv <c>Reserved</c> claim (partial unikal indeks da qoruyur);</item>
/// <item>bir təklifdən Bakı günü ərzində bir claim;</item>
/// <item>gündəlik və aylıq say — istifadəçinin aid olduğu kateqoriyalar arasında <b>ən səxavətli</b> limit.</item>
/// </list>
/// Yalnız <c>Reserved</c> və <c>Redeemed</c> claim-lər sayılır; bitmiş və ləğv olunmuş rezerv limiti yemir.
/// </summary>
public sealed class SocialLimits(IClaimRepository claims)
{
    /// <summary>Ən səxavətli gündəlik/aylıq limit (kateqoriya yoxdursa 0).</summary>
    public static (int PerDay, int PerMonth) MostGenerous(IEnumerable<Category> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        var list = categories.ToList();
        return list.Count == 0 ? (0, 0) : (list.Max(c => c.Limits.PerDay), list.Max(c => c.Limits.PerMonth));
    }

    public async Task<Result> CheckAsync(Guid userId, Offer offer, IReadOnlyList<Category> eligible, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var today = BakuTime.ToBakuDate(now);
        var startOfDay = BakuTime.StartOfDayUtc(today);
        var startOfMonth = BakuTime.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1));

        if (await claims.CountCountedSinceAsync(userId, startOfDay, offer.Id, cancellationToken).ConfigureAwait(false) > 0)
        {
            return SocialErrors.OfferAlreadyClaimedToday;
        }

        var (perDay, perMonth) = MostGenerous(eligible);
        if (await claims.CountCountedSinceAsync(userId, startOfDay, null, cancellationToken).ConfigureAwait(false) >= perDay)
        {
            return SocialErrors.DailyLimitReached;
        }

        if (await claims.CountCountedSinceAsync(userId, startOfMonth, null, cancellationToken).ConfigureAwait(false) >= perMonth)
        {
            return SocialErrors.MonthlyLimitReached;
        }

        return Result.Success();
    }

    /// <summary>
    /// Hovuz claim-i: gündəlik/aylıq say (təkliflərlə ortaq) və gündəlik hovuz xərci — ən səxavətli <c>poolSpendPerDayQepik</c>
    /// (yalnız <c>poolAccess</c> olan kateqoriyalar arasında).
    /// </summary>
    public async Task<Result> CheckPoolAsync(Guid userId, long priceQepik, IReadOnlyList<Category> eligible, IReadOnlyList<Category> poolCategories, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(poolCategories);
        var today = BakuTime.ToBakuDate(now);
        var startOfDay = BakuTime.StartOfDayUtc(today);
        var startOfMonth = BakuTime.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1));

        var (perDay, perMonth) = MostGenerous(eligible);
        if (await claims.CountCountedSinceAsync(userId, startOfDay, null, cancellationToken).ConfigureAwait(false) >= perDay)
        {
            return SocialErrors.DailyLimitReached;
        }

        if (await claims.CountCountedSinceAsync(userId, startOfMonth, null, cancellationToken).ConfigureAwait(false) >= perMonth)
        {
            return SocialErrors.MonthlyLimitReached;
        }

        var spendLimit = poolCategories.Count == 0 ? 0 : poolCategories.Max(c => c.Limits.PoolSpendPerDayQepik);
        var spent = await claims.SumPoolSpendSinceAsync(userId, startOfDay, cancellationToken).ConfigureAwait(false);
        return spent + priceQepik > spendLimit ? SocialErrors.PoolSpendLimitReached : Result.Success();
    }
}
