using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>Score consumer-i (E11-T01): xal yalnız təhvildə, idempotent, korporativ hədiyyə şirkətə bir dəfə.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class ScoreConsumerTests(PaymentsFixture fixture)
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Gift_redemption_scores_the_sender_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var message = Message(GiftRedeemed(GiftKind.Items, 9_00, units: 3, sender, corporateId: null));

        Assert.True(await ScoreAsync(host, message));
        Assert.False(await ScoreAsync(host, message)); // ikinci çatdırılma

        var score = Assert.Single(await EventsAsync(host, sender));
        Assert.Equal((ScoreSubjectType.User, 3, 1, 9_00L), (score.SubjectType, score.Points, score.Treats, score.Value));
    }

    [Fact]
    public async Task Partially_redeemed_corporate_gift_scores_the_company_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var corporate = Guid.CreateVersion7();
        var gift = GiftId.From(Guid.CreateVersion7());

        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Voucher, 10_00, 0, null, corporate, gift))));
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Voucher, 5_00, 0, null, corporate, gift))));

        var score = Assert.Single(await EventsAsync(host, corporate));
        Assert.Equal((ScoreSubjectType.Corporate, ScoringPolicy.GiftSource, gift.Value, 1), (score.SubjectType, score.SourceType, score.SourceId, score.Points));
    }

    [Fact]
    public async Task Daily_claim_scores_the_business_and_points_come_from_configuration()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var consumer = new ScoreConsumer(Options.Create(new LeaderboardOptions { Points = new ScorePoints { BusinessClaim = 4 } }));

        await consumer.HandleAsync(Message(ClaimRedeemed(business, amount: null, commissionable: false)), context, Ct);
        await context.SaveChangesAsync(Ct);

        Assert.Equal(4, Assert.Single(await EventsAsync(host, business)).Points);
    }

    [Fact]
    public async Task Score_events_are_append_only()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Voucher, 5_00, 0, sender, null))));

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        context.Remove(await context.Set<ScoreEvent>().SingleAsync(e => e.SubjectId == sender, Ct));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    private static GiftRedeemed GiftRedeemed(GiftKind kind, long value, int units, Guid? senderUserId, Guid? corporateId, GiftId? giftId = null) => new(
        giftId ?? GiftId.From(Guid.CreateVersion7()),
        GiftRedemptionId.From(Guid.CreateVersion7()),
        BusinessId.From(Guid.CreateVersion7()),
        kind,
        GiftScope.Business,
        value,
        units,
        senderUserId,
        corporateId is null ? null : Guid.CreateVersion7(),
        corporateId,
        Guid.CreateVersion7(),
        HasRecipientEmail: true,
        At);

    private static async Task<bool> ScoreAsync(ApiTestHost host, IntegrationMessage message)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await new InboxProcessor(context).ProcessAsync(new ScoreConsumer(Options.Create(new LeaderboardOptions())), message, host.Clock.UtcNow, Ct);
    }

    private static async Task<IReadOnlyList<ScoreEvent>> EventsAsync(ApiTestHost host, Guid subjectId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await context.Set<ScoreEvent>().AsNoTracking().Where(e => e.SubjectId == subjectId).ToListAsync(Ct);
    }
}
