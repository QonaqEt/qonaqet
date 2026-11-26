using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Caching;

namespace Qonaqet.IntegrationTests.Caching;

public sealed class ValkeyFixture : IAsyncLifetime
{
    public const string Image = "valkey/valkey:8.1.11-alpine";

    private IContainer? _container;

    public string? UnavailableReason { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(6379, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("valkey-cli", "ping"))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = $"{_container.Hostname}:{_container.GetMappedPublicPort(6379)}";
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
        }
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    /// <summary>Ayrı bağlantı = ayrı "instans" (hər test öz prefiksi ilə).</summary>
    public ValkeyConnection Connect(string prefix, string? connectionString = null) =>
        new(Options.Create(new ValkeyOptions { ConnectionString = connectionString ?? ConnectionString, KeyPrefix = prefix }));

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ValkeyCollection : ICollectionFixture<ValkeyFixture>
{
    public const string Name = "valkey";
}

/// <summary>Valkey keşi, kilidi və çox-instanslı rate limiter (E01-T10).</summary>
[Collection(ValkeyCollection.Name)]
public sealed class ValkeyTests(ValkeyFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Prefix() => "t" + Guid.NewGuid().ToString("N")[..8] + ":";

    [Fact]
    public async Task Cache_round_trips_values_and_misses_after_invalidation()
    {
        fixture.SkipIfUnavailable();
        await using var connection = fixture.Connect(Prefix());
        var cache = new ValkeyCache(connection, NullLogger<ValkeyCache>.Instance);

        Assert.Null(await cache.GetAsync<Sample>("k", Ct));
        await cache.SetAsync("k", new Sample("çay", 150), TimeSpan.FromMinutes(1), Ct);
        Assert.Equal(new Sample("çay", 150), await cache.GetAsync<Sample>("k", Ct));

        await cache.InvalidateAsync("k", Ct);
        Assert.Null(await cache.GetAsync<Sample>("k", Ct));
    }

    [Fact]
    public async Task GetOrSet_calls_the_source_only_on_a_miss()
    {
        fixture.SkipIfUnavailable();
        await using var connection = fixture.Connect(Prefix());
        var cache = new ValkeyCache(connection, NullLogger<ValkeyCache>.Instance);
        var calls = 0;

        Task<int> Source(CancellationToken _) => Task.FromResult(++calls);

        Assert.Equal(1, await cache.GetOrSetAsync("stats", TimeSpan.FromMinutes(1), Source, Ct));
        Assert.Equal(1, await cache.GetOrSetAsync("stats", TimeSpan.FromMinutes(1), Source, Ct));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Unreachable_valkey_is_a_cache_miss_not_a_failure()
    {
        await using var connection = fixture.Connect(Prefix(), "127.0.0.1:1");
        var cache = new ValkeyCache(connection, NullLogger<ValkeyCache>.Instance);
        var calls = 0;

        await cache.SetAsync("k", 1, TimeSpan.FromMinutes(1), Ct);
        Assert.Equal(0, await cache.GetAsync<int>("k", Ct));
        Assert.Equal(7, await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(1), _ => Task.FromResult(7 + calls++), Ct));

        var limiter = new ValkeyRateLimitStore(connection, NullLogger<ValkeyRateLimitStore>.Instance);
        Assert.True((await limiter.TryAcquireAsync("p", 1, TimeSpan.FromMinutes(1), Ct)).Allowed);
        Assert.Null(await new ValkeyDistributedLock(connection, NullLogger<ValkeyDistributedLock>.Instance).TryAcquireAsync("r", TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public async Task Lock_is_exclusive_across_instances_and_released_by_its_owner()
    {
        fixture.SkipIfUnavailable();
        var prefix = Prefix();
        await using var a = fixture.Connect(prefix);
        await using var b = fixture.Connect(prefix);
        var lockA = new ValkeyDistributedLock(a, NullLogger<ValkeyDistributedLock>.Instance);
        var lockB = new ValkeyDistributedLock(b, NullLogger<ValkeyDistributedLock>.Instance);

        var held = await lockA.TryAcquireAsync("payout:1", TimeSpan.FromSeconds(30), Ct);
        Assert.NotNull(held);
        Assert.Null(await lockB.TryAcquireAsync("payout:1", TimeSpan.FromSeconds(30), Ct));

        await held.DisposeAsync();
        await Task.Delay(20, Ct);
        var second = await lockB.TryAcquireAsync("payout:1", TimeSpan.FromSeconds(30), Ct);
        Assert.NotNull(second);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Lock_expires_on_its_own()
    {
        fixture.SkipIfUnavailable();
        await using var connection = fixture.Connect(Prefix());
        var distributedLock = new ValkeyDistributedLock(connection, NullLogger<ValkeyDistributedLock>.Instance);

        Assert.NotNull(await distributedLock.TryAcquireAsync("job", TimeSpan.FromMilliseconds(200), Ct));
        await Task.Delay(400, Ct);
        Assert.NotNull(await distributedLock.TryAcquireAsync("job", TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public async Task Rate_limit_is_shared_by_all_instances_and_atomic_under_concurrency()
    {
        fixture.SkipIfUnavailable();
        var prefix = Prefix();
        await using var a = fixture.Connect(prefix);
        await using var b = fixture.Connect(prefix);
        var stores = new[]
        {
            new ValkeyRateLimitStore(a, NullLogger<ValkeyRateLimitStore>.Instance),
            new ValkeyRateLimitStore(b, NullLogger<ValkeyRateLimitStore>.Instance),
        };

        var decisions = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            stores[i % 2].TryAcquireAsync("otp:a@b.az", 10, TimeSpan.FromMinutes(1), Ct)));

        Assert.Equal(10, decisions.Count(d => d.Allowed));
        Assert.All(decisions.Where(d => !d.Allowed), d => Assert.InRange(d.RetryAfter, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Aspnet_rate_limiter_middleware_rejects_over_the_limit()
    {
        fixture.SkipIfUnavailable();
        var connectionString = fixture.ConnectionString;
        var prefix = Prefix();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_ => fixture.Connect(prefix, connectionString));
        builder.Services.AddSingleton<IRateLimitStore>(sp => new ValkeyRateLimitStore(sp.GetRequiredService<ValkeyConnection>(), NullLogger<ValkeyRateLimitStore>.Instance));
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddValkeyFixedWindowPolicy("otp", _ => "client", 2, TimeSpan.FromMinutes(1));
        });
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/otp", () => "ok").RequireRateLimiting("otp");
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();

        var codes = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync(new Uri("/otp", UriKind.Relative), Ct);
            codes.Add((int)response.StatusCode);
        }

        Assert.Equal([200, 200, 429], codes);
    }

    [Fact]
    public async Task Leaderboard_sorted_set_is_replaced_atomically_ranked_and_expires()
    {
        fixture.SkipIfUnavailable();
        await using var connection = fixture.Connect(Prefix());
        var store = new ValkeyLeaderboardStore(connection, NullLogger<ValkeyLeaderboardStore>.Instance);
        var board = new LeaderboardBoard(ScoreSubjectType.User, LeaderboardPeriodType.Week, "2026-W48");
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(await store.TopAsync(board, 10, a, Ct));
        await store.SetIfCachedAsync(board, new LeaderboardScore(a, 50), Ct);
        Assert.Null(await store.TopAsync(board, 10, a, Ct)); // keşdə olmayan lövhə yaradılmır

        await store.ReplaceAsync(board, [new LeaderboardScore(a, 3), new LeaderboardScore(b, 9), new LeaderboardScore(c, 3)], Ct);
        await store.SetIfCachedAsync(board, new LeaderboardScore(a, 4), Ct);
        var snapshot = await store.TopAsync(board, 2, a, Ct);

        Assert.Equal([new LeaderboardScore(b, 9), new LeaderboardScore(a, 4)], snapshot!.Top);
        Assert.Equal((2, 4), (snapshot.Rank, snapshot.Points));
        var db = await connection.DatabaseAsync();
        var ttl = await db.KeyTimeToLiveAsync(connection.Prefix + board.CacheKey);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(1), ValkeyLeaderboardStore.TimeToLive);
    }

    [Fact]
    public async Task Leaderboard_ties_follow_reverse_member_order()
    {
        fixture.SkipIfUnavailable();
        await using var connection = fixture.Connect(Prefix());
        var store = new ValkeyLeaderboardStore(connection, NullLogger<ValkeyLeaderboardStore>.Instance);
        var board = new LeaderboardBoard(ScoreSubjectType.Business, LeaderboardPeriodType.AllTime, "all");
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();

        await store.ReplaceAsync(board, [.. ids.Select(id => new LeaderboardScore(id, 2))], Ct);

        Assert.Equal(
            ids.Select(id => id.ToString()).OrderDescending(StringComparer.Ordinal),
            (await store.TopAsync(board, 10, null, Ct))!.Top.Select(s => s.SubjectId.ToString()));
    }

    [Fact]
    public async Task Unreachable_valkey_leaderboard_is_a_miss()
    {
        await using var connection = fixture.Connect(Prefix(), "127.0.0.1:1");
        var store = new ValkeyLeaderboardStore(connection, NullLogger<ValkeyLeaderboardStore>.Instance);
        var board = new LeaderboardBoard(ScoreSubjectType.User, LeaderboardPeriodType.AllTime, "all");

        await store.ReplaceAsync(board, [new LeaderboardScore(Guid.NewGuid(), 1)], Ct);
        await store.SetIfCachedAsync(board, new LeaderboardScore(Guid.NewGuid(), 1), Ct);
        Assert.Null(await store.TopAsync(board, 10, null, Ct));
    }

    private sealed record Sample(string Name, long AmountQepik);
}
