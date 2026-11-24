using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Common;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>Lövhə oxuyucusu (E11-T03): keş boşdursa Postgres-dən qurur; Valkey heç vaxt həqiqət mənbəyi deyil.</summary>
public sealed class LeaderboardReaderTests
{
    private static readonly LeaderboardBoard Board = new(ScoreSubjectType.User, LeaderboardPeriodType.Month, "2026-11");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Empty_cache_is_rebuilt_from_postgres_and_the_next_read_is_served_from_the_cache()
    {
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var postgres = new FakeEntries(new(a, 5), new(b, 12), new(c, 7));
        var store = new InMemoryLeaderboardStore();
        var reader = new LeaderboardReader(store, postgres);

        var first = await reader.ReadAsync(Board, 2, c, Ct);

        Assert.Equal([b, c], first.Top.Select(s => s.SubjectId));
        Assert.Equal((2, 7), (first.Rank, first.Points));
        Assert.True(store.IsCached(Board));

        var second = await reader.ReadAsync(Board, 2, c, Ct);
        Assert.Equal(first.Top, second.Top);
        Assert.Equal(1, postgres.Reads);
    }

    [Fact]
    public async Task Unavailable_cache_still_serves_the_postgres_board()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var postgres = new FakeEntries(new(a, 3), new(b, 9));
        var reader = new LeaderboardReader(new InMemoryLeaderboardStore { Unavailable = true }, postgres);

        var board = await reader.ReadAsync(Board, 100, a, Ct);

        Assert.Equal([new LeaderboardScore(b, 9), new LeaderboardScore(a, 3)], board.Top);
        Assert.Equal((2, 3), (board.Rank, board.Points));
    }

    [Fact]
    public async Task Lost_or_wrong_cache_is_replaced_by_postgres_once_the_key_expires()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var postgres = new FakeEntries(new(a, 3), new(b, 9));
        var store = new InMemoryLeaderboardStore();
        var reader = new LeaderboardReader(store, postgres);
        await reader.ReadAsync(Board, 100, null, Ct);

        store.Corrupt(Board, a, 1_000);
        store.Expire(Board);

        var board = await reader.ReadAsync(Board, 100, null, Ct);
        Assert.Equal([new LeaderboardScore(b, 9), new LeaderboardScore(a, 3)], board.Top);
    }

    [Fact]
    public async Task Ties_are_ordered_the_same_way_as_a_valkey_sorted_set()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var postgres = new FakeEntries([.. ids.Select(id => new LeaderboardScore(id, 4))]);
        var store = new InMemoryLeaderboardStore();
        var reader = new LeaderboardReader(store, postgres);

        var fromPostgres = await reader.ReadAsync(Board, 100, ids[2], Ct);
        var fromCache = await reader.ReadAsync(Board, 100, ids[2], Ct);

        Assert.Equal(ids.Select(id => id.ToString()).OrderDescending(StringComparer.Ordinal), fromPostgres.Top.Select(s => s.SubjectId.ToString()));
        Assert.Equal(fromPostgres.Top, fromCache.Top);
        Assert.Equal((fromPostgres.Rank, fromPostgres.Points), (fromCache.Rank, fromCache.Points));
    }

    [Fact]
    public void Periods_use_the_baku_calendar_and_iso_weeks()
    {
        var lateSunday = new DateTimeOffset(2026, 11, 29, 20, 30, 0, TimeSpan.Zero); // Bakı: 30 noyabr (bazar ertəsi) 00:30

        Assert.Equal(
            [new LeaderboardPeriod(LeaderboardPeriodType.Week, "2026-W49"), new LeaderboardPeriod(LeaderboardPeriodType.Month, "2026-11"), LeaderboardPeriod.AllTime],
            LeaderboardPeriod.For(lateSunday));
        Assert.Equal("2027-W52", LeaderboardPeriod.Current(LeaderboardPeriodType.Week, new DateTimeOffset(2028, 1, 1, 12, 0, 0, TimeSpan.Zero)).Key);
        Assert.Equal("lb:Business:Week:2026-W49", new LeaderboardBoard(ScoreSubjectType.Business, LeaderboardPeriodType.Week, "2026-W49").CacheKey);
    }

    private sealed class FakeEntries(params LeaderboardScore[] scores) : ILeaderboardRepository
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<LeaderboardScore>>(scores);
        }

        public Task<IReadOnlyDictionary<LeaderboardPeriod, int>> PointsAsync(
            ScoreSubjectType subjectType, Guid subjectId, IReadOnlyCollection<LeaderboardPeriod> periods, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ScoreEvent>> HistoryAsync(ScoreSubjectType subjectType, Guid subjectId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> BadgesAwardedAsync(ScoreSubjectType subjectType, BadgeType badge, string periodKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BadgeAward>> BadgesAsync(ScoreSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddBadgesAsync(IReadOnlyCollection<BadgeAward> awards, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LeaderboardRecompute> RecomputeAsync(LeaderboardBoard board, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
