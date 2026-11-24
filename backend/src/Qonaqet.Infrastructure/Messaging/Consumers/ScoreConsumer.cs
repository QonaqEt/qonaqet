using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Reytinq parametrləri (<c>Leaderboard</c> bölməsi).</summary>
public sealed class LeaderboardOptions
{
    public const string SectionName = "Leaderboard";

    /// <summary>Xal dəyərləri (<c>Leaderboard:Points</c>).</summary>
    public ScorePoints Points { get; set; } = new();

    /// <summary>Anti-gaming limitləri (<c>Leaderboard:Limits</c>).</summary>
    public ScoreLimits Limits { get; set; } = new();
}

/// <summary>
/// Xal consumer-i: təhvil hadisələrindən (<see cref="GiftRedeemed"/>, <see cref="ClaimRedeemed"/>) <c>score_events</c> yazır, inbox qeydi ilə
/// <b>eyni</b> Postgres tranzaksiyasında. Xal yalnız təhvildə verilir: alınıb istifadə olunmayan hədiyyə xal vermir. İkiqat qoruma: inbox
/// (mesaj + consumer) və (<c>subject_type</c>, <c>subject_id</c>, <c>source_type</c>, <c>source_id</c>) unikal indeksi — məs. hissəli
/// təhvil olunan korporativ hədiyyə şirkətə yalnız bir dəfə xal verir.
/// İstifadəçi xalına anti-gaming limitləri tətbiq olunur (<see cref="ScoreLimits"/>): Bakı günündə 300, göndərən→alıcı cütü üçün Bakı
/// ayında 10. Limitdən artıq qalan hadisə yenə yazılır (qonaqlıq sayılır), sadəcə xalı azaldılır və ya <c>0</c> olur.
/// </summary>
public sealed class ScoreConsumer(IOptions<LeaderboardOptions> options) : IIntegrationEventConsumer
{
    public static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;

    public static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;

    public string Name => "score";

    public IReadOnlyCollection<string> EventTypes { get; } = [GiftRedeemedType, ClaimRedeemedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var policy = new ScoringPolicy(options.Value.Points);
        IReadOnlyList<ScoreAward> awards;
        DateTimeOffset at;
        if (message.Type == GiftRedeemedType)
        {
            var gift = Read<GiftRedeemedPayload>(message);
            awards = policy.ForGift(new RedeemedGift(
                gift.GiftId, gift.RedemptionId, gift.Kind, gift.ValueQepik, gift.Units, gift.SenderUserId, gift.CorporateId, gift.RecipientUserId));
            at = gift.At;
        }
        else if (message.Type == ClaimRedeemedType)
        {
            var claim = Read<ClaimRedeemedPayload>(message);
            awards = policy.ForClaim(new RedeemedClaim(
                claim.ClaimId, claim.BusinessId, claim.OfferType, claim.AmountQepik, claim.FunderUserId, claim.FunderCorporateId));
            at = claim.At;
        }
        else
        {
            return;
        }

        var events = context.Set<ScoreEvent>();
        foreach (var award in awards)
        {
            var exists = await events.AnyAsync(
                x => x.SubjectType == award.SubjectType && x.SubjectId == award.SubjectId && x.SourceType == award.SourceType && x.SourceId == award.SourceId,
                cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                var points = award.SubjectType == ScoreSubjectType.User ? await CappedAsync(events, award, at, cancellationToken).ConfigureAwait(false) : award.Points;
                events.Add(ScoreEvent.Award(
                    award.SubjectType, award.SubjectId, award.SourceType, award.SourceId, points, treats: 1, award.Value, at, award.RecipientUserId));
            }
        }
    }

    private async Task<int> CappedAsync(DbSet<ScoreEvent> events, ScoreAward award, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var day = BakuTime.ToBakuDate(at);
        var dayStart = BakuTime.StartOfDayUtc(day);
        var dayEnd = BakuTime.EndOfDayUtc(day);
        var userToday = await events
            .Where(x => x.SubjectType == ScoreSubjectType.User && x.SubjectId == award.SubjectId && x.CreatedAt >= dayStart && x.CreatedAt < dayEnd)
            .SumAsync(x => x.Points, cancellationToken).ConfigureAwait(false);

        int? pairThisMonth = null;
        if (award.RecipientUserId is { } recipientId)
        {
            var monthStart = BakuTime.StartOfDayUtc(new DateOnly(day.Year, day.Month, 1));
            var monthEnd = BakuTime.StartOfDayUtc(new DateOnly(day.Year, day.Month, 1).AddMonths(1));
            pairThisMonth = await events
                .Where(x => x.SubjectType == ScoreSubjectType.User && x.SubjectId == award.SubjectId && x.RecipientUserId == recipientId
                    && x.CreatedAt >= monthStart && x.CreatedAt < monthEnd)
                .SumAsync(x => x.Points, cancellationToken).ConfigureAwait(false);
        }

        return options.Value.Limits.Allow(award.Points, pairThisMonth, userToday);
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record GiftRedeemedPayload(
        Guid GiftId,
        Guid RedemptionId,
        GiftKind Kind,
        long ValueQepik,
        int Units,
        Guid? SenderUserId,
        Guid? CorporateId,
        Guid? RecipientUserId,
        DateTimeOffset At);

    private sealed record ClaimRedeemedPayload(
        Guid ClaimId, Guid BusinessId, OfferType? OfferType, long? AmountQepik, Guid? FunderUserId, Guid? FunderCorporateId, DateTimeOffset At);
}
