using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Repositories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Reytinq parametrləri (<c>Leaderboard</c> bölməsi).</summary>
public sealed class LeaderboardOptions
{
    public const string SectionName = "Leaderboard";

    /// <summary>Xal dəyərlərinin default-u (<c>Leaderboard:Points</c>); admin təyin edibsə <c>score_point_settings</c> qüvvədədir.</summary>
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
/// Eyni tranzaksiyada <c>leaderboard_entries</c> aqreqatları upsert olunur (<see cref="LeaderboardAggregates"/>); commit-dən sonra Valkey
/// lövhələrindəki xal mütləq dəyərlə yenilənir (yalnız keşdə olan lövhələr — qalanını oxu Postgres-dən qurur).
/// </summary>
public sealed class ScoreConsumer(IOptions<LeaderboardOptions> options, ILeaderboardStore store) : IIntegrationEventConsumer, IAfterCommitConsumer
{
    public static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;

    public static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;

    public static readonly string GiftRedemptionReversedType = typeof(GiftRedemptionReversed).FullName!;

    public static readonly string ClaimRedemptionReversedType = typeof(ClaimRedemptionReversed).FullName!;

    public string Name => "score";

    public IReadOnlyCollection<string> EventTypes { get; } = [GiftRedeemedType, ClaimRedeemedType, GiftRedemptionReversedType, ClaimRedemptionReversedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (sourceType, sourceId) in ReversedSources(message))
        {
            await ScoreReversal.ReverseAsync(context, sourceType, sourceId, cancellationToken).ConfigureAwait(false);
        }

        var (awards, at) = await AwardsAsync(message, context, cancellationToken).ConfigureAwait(false);
        var events = context.Set<ScoreEvent>();
        foreach (var award in awards)
        {
            var exists = await events.AnyAsync(
                x => x.SubjectType == award.SubjectType && x.SubjectId == award.SubjectId && x.SourceType == award.SourceType && x.SourceId == award.SourceId,
                cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                var points = award.SubjectType == ScoreSubjectType.User ? await CappedAsync(events, award, at, cancellationToken).ConfigureAwait(false) : award.Points;
                var scoreEvent = ScoreEvent.Award(
                    award.SubjectType, award.SubjectId, award.SourceType, award.SourceId, points, treats: 1, award.Value, at, award.RecipientUserId);
                await LeaderboardAggregates.AddAsync(context, scoreEvent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Commit-dən sonra: qazananların hadisə dövrlərindəki yeni xalı keşlənmiş lövhələrə yazılır (idempotent, mütləq dəyər).</summary>
    public async Task AfterCommitAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var (awards, at) = await AwardsAsync(message, context, cancellationToken).ConfigureAwait(false);
        var subjects = awards.Select(a => (a.SubjectType, a.SubjectId)).ToList();
        foreach (var (sourceType, sourceId) in ReversedSources(message))
        {
            // Geri almada təsirlənən qazananlar: orijinal xal hadisələrinin sahibləri; dövrlər orijinal hadisənin vaxtına görədir.
            var originals = await context.Set<ScoreEvent>().AsNoTracking()
                .Where(e => e.SourceType == sourceType && e.SourceId == sourceId)
                .Select(e => new { e.SubjectType, e.SubjectId, e.CreatedAt }).ToListAsync(cancellationToken).ConfigureAwait(false);
            subjects.AddRange(originals.Select(o => (o.SubjectType, o.SubjectId)));
            if (originals.Count > 0)
            {
                at = originals[0].CreatedAt;
            }
        }

        foreach (var subject in subjects.Distinct())
        {
            var current = await LeaderboardAggregates.CurrentAsync(context, subject.SubjectType, subject.SubjectId, at, cancellationToken).ConfigureAwait(false);
            foreach (var (board, score) in current)
            {
                await store.SetIfCachedAsync(board, score, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Xal dəyərləri təhvil anında qüvvədə olan versiyadır (təkrar çatdırılma eyni nəticəni verir).</summary>
    private async Task<(IReadOnlyList<ScoreAward> Awards, DateTimeOffset At)> AwardsAsync(
        IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        if (message.Type == GiftRedeemedType)
        {
            var gift = Read<GiftRedeemedPayload>(message);
            var policy = await PolicyAsync(context, gift.At, cancellationToken).ConfigureAwait(false);
            return (policy.ForGift(new RedeemedGift(
                gift.GiftId, gift.RedemptionId, gift.Kind, gift.ValueQepik, gift.Units, gift.SenderUserId, gift.CorporateId, gift.RecipientUserId)), gift.At);
        }

        if (message.Type == ClaimRedeemedType)
        {
            var claim = Read<ClaimRedeemedPayload>(message);
            var policy = await PolicyAsync(context, claim.At, cancellationToken).ConfigureAwait(false);
            return (policy.ForClaim(new RedeemedClaim(
                claim.ClaimId, claim.BusinessId, claim.OfferType, claim.AmountQepik, claim.FunderUserId, claim.FunderCorporateId)), claim.At);
        }

        return ([], default);
    }

    /// <summary>Geri alma hadisəsinin xal mənbələri: hədiyyədə təhvil (və hədiyyə tam təhvilsiz qalıbsa şirkətin hədiyyə xalı), claim-də claim.</summary>
    private static IReadOnlyList<(string SourceType, Guid SourceId)> ReversedSources(IntegrationMessage message)
    {
        if (message.Type == GiftRedemptionReversedType)
        {
            var reversed = Read<GiftRedemptionReversedPayload>(message);
            return reversed.FullyUnredeemed
                ? [(ScoringPolicy.GiftRedemptionSource, reversed.RedemptionId), (ScoringPolicy.GiftSource, reversed.GiftId)]
                : [(ScoringPolicy.GiftRedemptionSource, reversed.RedemptionId)];
        }

        return message.Type == ClaimRedemptionReversedType ? [(ScoringPolicy.ClaimSource, Read<ClaimReversedPayload>(message).ClaimId)] : [];
    }

    private async Task<ScoringPolicy> PolicyAsync(QonaqetDbContext context, DateTimeOffset at, CancellationToken cancellationToken) =>
        new((await ScorePointSettings.AtAsync(context, options.Value.Points, at, cancellationToken).ConfigureAwait(false)).Points);

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

    private sealed record GiftRedemptionReversedPayload(Guid GiftId, Guid RedemptionId, bool FullyUnredeemed, DateTimeOffset At);

    private sealed record ClaimReversedPayload(Guid ClaimId, DateTimeOffset At);

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
