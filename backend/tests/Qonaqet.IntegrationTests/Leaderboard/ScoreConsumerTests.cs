using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Common;
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

/// <summary>
/// Score consumer-i (E11-T01): xal yalnız təhvildə, idempotent, korporativ hədiyyə şirkətə bir dəfə.
/// Anti-gaming (E11-T02): özünə hədiyyə və yalnız-link hədiyyə xal vermir, gündəlik və cüt limitləri, geri alma mənfi hadisə yazır.
/// </summary>
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
        var consumer = new ScoreConsumer(Options.Create(new LeaderboardOptions { Points = new ScorePoints { BusinessClaim = 4 } }), new InMemoryLeaderboardStore());

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

    [Fact]
    public async Task Self_gift_and_link_only_gift_give_the_sender_no_points()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();

        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 4_00, 2, sender, null, recipient: sender))));
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 4_00, 2, sender, null) with { RecipientUserId = null })));

        Assert.Empty(await EventsAsync(host, sender));
    }

    [Fact]
    public async Task Daily_limit_records_the_treat_but_caps_the_points_per_baku_day()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var limits = new LeaderboardOptions { Limits = new ScoreLimits { UserPerDay = 5 } };
        var afterMidnight = new DateTimeOffset(2026, 11, 24, 20, 30, 0, TimeSpan.Zero); // Bakı: 25 noyabr 00:30

        await HandleAsync(host, limits, GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null, at: At));
        await HandleAsync(host, limits, GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null, at: At.AddHours(1)));
        await HandleAsync(host, limits, GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null, at: At.AddHours(2)));
        await HandleAsync(host, limits, GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null, at: afterMidnight));

        var events = await EventsAsync(host, sender);
        Assert.Equal([3, 2, 0, 3], events.OrderBy(e => e.CreatedAt).Select(e => e.Points));
        Assert.Equal(4, events.Sum(e => e.Treats));
    }

    [Fact]
    public async Task Same_sender_and_recipient_earn_at_most_ten_points_per_baku_month()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var friend = Guid.CreateVersion7();
        var options = new LeaderboardOptions();

        await HandleAsync(host, options, GiftRedeemed(GiftKind.Items, 8_00, 4, sender, null, recipient: friend, at: At));
        await HandleAsync(host, options, GiftRedeemed(GiftKind.Items, 8_00, 4, sender, null, recipient: friend, at: At.AddDays(1)));
        await HandleAsync(host, options, GiftRedeemed(GiftKind.Items, 8_00, 4, sender, null, recipient: friend, at: At.AddDays(2)));
        await HandleAsync(host, options, GiftRedeemed(GiftKind.Items, 8_00, 4, sender, null, at: At.AddDays(3))); // başqa alıcı
        await HandleAsync(host, options, GiftRedeemed(GiftKind.Items, 8_00, 4, sender, null, recipient: friend, at: At.AddDays(7))); // dekabr

        Assert.Equal([4, 4, 2, 4, 4], (await EventsAsync(host, sender)).OrderBy(e => e.CreatedAt).Select(e => e.Points));
    }

    [Fact]
    public async Task Reversed_redemption_writes_negative_score_events_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var redeemed = GiftRedeemed(GiftKind.Items, 9_00, 3, sender, null);
        Assert.True(await ScoreAsync(host, Message(redeemed)));

        for (var i = 0; i < 2; i++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            await ScoreReversal.ReverseAsync(context, ScoringPolicy.GiftRedemptionSource, redeemed.RedemptionId.Value, Ct);
            await context.SaveChangesAsync(Ct);
        }

        var events = await EventsAsync(host, sender);
        Assert.Equal(2, events.Count);
        Assert.Equal((0, 0, 0L), (events.Sum(e => e.Points), events.Sum(e => e.Treats), events.Sum(e => e.Value)));
        Assert.Equal(-3, Assert.Single(events, e => e.IsReversal).Points);
    }

    [Fact]
    public async Task Aggregates_are_upserted_per_week_month_and_all_time_and_a_reversal_subtracts()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var tuesday = GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null, at: At); // 2026-W48
        var nextWeek = GiftRedeemed(GiftKind.Voucher, 9_00, 0, sender, null, at: At.AddDays(6)); // 30 noyabr, 2026-W49

        Assert.True(await ScoreAsync(host, Message(tuesday)));
        Assert.True(await ScoreAsync(host, Message(nextWeek)));
        Assert.False(await ScoreAsync(host, Message(nextWeek))); // dublikat aqreqatı ikiqat artırmır

        Assert.Equal(
            [("AllTime:all", 5, 2, 15_00L), ("Month:2026-11", 5, 2, 15_00L), ("Week:2026-W48", 3, 1, 6_00L), ("Week:2026-W49", 2, 1, 9_00L)],
            Rows(await EntriesAsync(host, sender)));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            await ScoreReversal.ReverseAsync(context, ScoringPolicy.GiftRedemptionSource, tuesday.RedemptionId.Value, Ct);
            await context.SaveChangesAsync(Ct);
        }

        Assert.Equal(
            [("AllTime:all", 2, 1, 9_00L), ("Month:2026-11", 2, 1, 9_00L), ("Week:2026-W48", 0, 0, 0L), ("Week:2026-W49", 2, 1, 9_00L)],
            Rows(await EntriesAsync(host, sender)));

        static IEnumerable<(string, int, int, long)> Rows(IEnumerable<LeaderboardEntry> entries) =>
            entries.Select(e => ($"{e.PeriodType}:{e.PeriodKey}", e.Points, e.Treats, e.Value)).Order();
    }

    [Fact]
    public async Task Cached_boards_get_the_new_total_after_commit_and_uncached_boards_stay_empty()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = Guid.CreateVersion7();
        var rival = Guid.CreateVersion7();
        var month = new LeaderboardBoard(ScoreSubjectType.User, LeaderboardPeriodType.Month, "2026-11");
        var week = new LeaderboardBoard(ScoreSubjectType.User, LeaderboardPeriodType.Week, "2026-W48");
        var store = new InMemoryLeaderboardStore();
        await store.ReplaceAsync(month, [new LeaderboardScore(rival, 4)], Ct);

        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null)), store));
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 6_00, 3, sender, null)), store));

        var top = await store.TopAsync(month, 10, sender, Ct);
        Assert.Equal([new LeaderboardScore(sender, 6), new LeaderboardScore(rival, 4)], top!.Top);
        Assert.False(store.IsCached(week));
    }

    [Fact]
    public async Task Reader_rebuilds_the_board_from_postgres_through_the_api_services()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (first, second) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 2_00, 1, first, null))));
        Assert.True(await ScoreAsync(host, Message(GiftRedeemed(GiftKind.Items, 8_00, 4, second, null))));

        await using var scope = host.Services.CreateAsyncScope();
        var store = new InMemoryLeaderboardStore();
        var reader = new LeaderboardReader(store, scope.ServiceProvider.GetRequiredService<ILeaderboardRepository>());
        var board = await reader.ReadAsync(new LeaderboardBoard(ScoreSubjectType.User, LeaderboardPeriodType.AllTime, "all"), 100, first, Ct);

        Assert.Equal([new LeaderboardScore(second, 4), new LeaderboardScore(first, 1)], board.Top);
        Assert.Equal((2, 1), (board.Rank, board.Points));
        Assert.Equal(1, store.Replacements);
    }

    private static GiftRedeemed GiftRedeemed(
        GiftKind kind,
        long value,
        int units,
        Guid? senderUserId,
        Guid? corporateId,
        GiftId? giftId = null,
        Guid? recipient = null,
        DateTimeOffset? at = null) => new(
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
        recipient ?? Guid.CreateVersion7(),
        HasRecipientEmail: true,
        at ?? At);

    private static async Task HandleAsync(ApiTestHost host, LeaderboardOptions options, GiftRedeemed redeemed)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await new ScoreConsumer(Options.Create(options), new InMemoryLeaderboardStore()).HandleAsync(Message(redeemed), context, Ct);
        await context.SaveChangesAsync(Ct);
    }

    private static async Task<bool> ScoreAsync(ApiTestHost host, IntegrationMessage message, InMemoryLeaderboardStore? store = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var consumer = new ScoreConsumer(Options.Create(new LeaderboardOptions()), store ?? new InMemoryLeaderboardStore());
        return await new InboxProcessor(context).ProcessAsync(consumer, message, host.Clock.UtcNow, Ct);
    }

    private static async Task<IReadOnlyList<LeaderboardEntry>> EntriesAsync(ApiTestHost host, Guid subjectId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await context.Set<LeaderboardEntry>().AsNoTracking().Where(e => e.SubjectId == subjectId).ToListAsync(Ct);
    }

    private static async Task<IReadOnlyList<ScoreEvent>> EventsAsync(ApiTestHost host, Guid subjectId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await context.Set<ScoreEvent>().AsNoTracking().Where(e => e.SubjectId == subjectId).ToListAsync(Ct);
    }
}
