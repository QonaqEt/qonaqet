using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>JWT ifrazı və refresh token rotasiyası (E02-T02).</summary>
[Collection(MongoCollection.Name)]
public sealed class SessionTests(MongoFixture mongo)
{
    private const string Email = "kamran@example.az";

    [Fact]
    public async Task Login_returns_a_short_lived_access_token_that_authenticates_the_user()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        var login = await host.LoginAsync(Email);
        var tokens = login.GetProperty("tokens");

        var accessExpires = tokens.GetProperty("accessTokenExpiresAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromMinutes(15), accessExpires - host.Clock.UtcNow);
        Assert.Equal(TimeSpan.FromDays(30), tokens.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset() - host.Clock.UtcNow);

        using var whoami = await host.GetAsync("/__test/auth/whoami", tokens.GetProperty("accessToken").GetString());
        var me = await host.JsonAsync(whoami);
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        Assert.Equal(login.GetProperty("userId").GetString(), me.GetProperty("sub").GetString());
        Assert.Equal(["User"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Refresh_token_is_stored_only_as_a_hash()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        var raw = RefreshToken(await host.LoginAsync(Email));

        var stored = await host.Database.GetCollection<BsonDocument>("refreshTokens").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(raw, stored.ToJson(), StringComparison.Ordinal);
        Assert.Equal(64, stored["tokenHash"].AsString.Length);
    }

    [Fact]
    public async Task Each_refresh_revokes_the_old_token_and_issues_a_new_pair()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var first = RefreshToken(await host.LoginAsync(Email));

        using var refreshed = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = first });
        var pair = await host.JsonAsync(refreshed);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = pair.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);
        using (var whoami = await host.GetAsync("/__test/auth/whoami", pair.GetProperty("accessToken").GetString()))
        {
            Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        }

        var tokens = await host.Database.GetCollection<BsonDocument>("refreshTokens").Find(new BsonDocument()).SortBy(d => d["createdAt"]).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, tokens.Count);
        var old = tokens.Single(t => !t["revokedAt"].IsBsonNull);
        var current = tokens.Single(t => t["revokedAt"].IsBsonNull);
        Assert.Equal("Rotated", old["revokeReason"].AsString);
        Assert.Equal(current["_id"], old["replacedById"]);

        using var third = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = second });
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_closes_every_session_of_the_user()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var stolen = RefreshToken(await host.LoginAsync(Email));
        var otherDevice = RefreshToken(await host.LoginAsync(Email));
        using var rotated = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = stolen });
        var legitimate = (await host.JsonAsync(rotated)).GetProperty("refreshToken").GetString();

        using var replay = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = stolen });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("refresh_token_reused", (await host.JsonAsync(replay)).GetProperty("code").GetString());
        foreach (var token in new[] { legitimate, otherDevice })
        {
            using var attempt = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = token });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var active = await host.Database.GetCollection<BsonDocument>("refreshTokens")
            .CountDocumentsAsync(new BsonDocument("revokedAt", BsonNull.Value), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RefreshToken(await host.LoginAsync(Email));

        using var logout = await host.PostAsync("/api/v1/auth/logout", new { refreshToken = token });
        using var again = await host.PostAsync("/api/v1/auth/logout", new { refreshToken = token });
        using var unknown = await host.PostAsync("/api/v1/auth/logout", new { refreshToken = "does-not-exist" });
        using var refresh = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = token });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var stored = await host.Database.GetCollection<BsonDocument>("refreshTokens").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Logout", stored["revokeReason"].AsString);
    }

    [Fact]
    public async Task Expired_tokens_are_rejected()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var realNow = host.Clock.UtcNow;
        host.Clock.UtcNow = realNow.AddDays(-31);
        var login = await host.LoginAsync(Email);
        host.Clock.UtcNow = realNow;

        using var whoami = await host.GetAsync("/__test/auth/whoami", login.GetProperty("tokens").GetProperty("accessToken").GetString());
        using var refresh = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = RefreshToken(login) });

        Assert.Equal(HttpStatusCode.Unauthorized, whoami.StatusCode);
        Assert.Equal("refresh_token_invalid", (await host.JsonAsync(refresh)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Garbage_and_tampered_access_tokens_leave_the_request_anonymous()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var access = (await host.LoginAsync(Email)).GetProperty("tokens").GetProperty("accessToken").GetString()!;

        using var garbage = await host.GetAsync("/__test/auth/whoami", "not-a-jwt");
        using var tampered = await host.GetAsync("/__test/auth/whoami", access[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
    }

    private static string RefreshToken(JsonElement login) => login.GetProperty("tokens").GetProperty("refreshToken").GetString()!;
}
