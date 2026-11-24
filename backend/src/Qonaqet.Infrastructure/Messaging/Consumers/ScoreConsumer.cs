using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Reytinq parametrləri (<c>Leaderboard</c> bölməsi).</summary>
public sealed class LeaderboardOptions
{
    public const string SectionName = "Leaderboard";

    /// <summary>Xal dəyərləri (<c>Leaderboard:Points</c>).</summary>
    public ScorePoints Points { get; set; } = new();
}

/// <summary>
/// Xal consumer-i: təhvil hadisələrindən (<see cref="GiftRedeemed"/>, <see cref="ClaimRedeemed"/>) <c>score_events</c> yazır, inbox qeydi ilə
/// <b>eyni</b> Postgres tranzaksiyasında. Xal yalnız təhvildə verilir: alınıb istifadə olunmayan hədiyyə xal vermir. İkiqat qoruma: inbox
/// (mesaj + consumer) və (<c>subject_type</c>, <c>subject_id</c>, <c>source_type</c>, <c>source_id</c>) unikal indeksi — məs. hissəli
/// təhvil olunan korporativ hədiyyə şirkətə yalnız bir dəfə xal verir.
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
            awards = policy.ForGift(new RedeemedGift(gift.GiftId, gift.RedemptionId, gift.Kind, gift.ValueQepik, gift.Units, gift.SenderUserId, gift.CorporateId));
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
                events.Add(ScoreEvent.Award(award.SubjectType, award.SubjectId, award.SourceType, award.SourceId, award.Points, treats: 1, award.Value, at));
            }
        }
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record GiftRedeemedPayload(
        Guid GiftId, Guid RedemptionId, GiftKind Kind, long ValueQepik, int Units, Guid? SenderUserId, Guid? CorporateId, DateTimeOffset At);

    private sealed record ClaimRedeemedPayload(
        Guid ClaimId, Guid BusinessId, OfferType? OfferType, long? AmountQepik, Guid? FunderUserId, Guid? FunderCorporateId, DateTimeOffset At);
}
