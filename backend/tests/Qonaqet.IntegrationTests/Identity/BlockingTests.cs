using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>İstifadəçi bloklama (E02-T06).</summary>
[Collection(MongoCollection.Name)]
public sealed class BlockingTests(MongoFixture mongo)
{
    private const string Email = "blocked@example.az";

    [Fact]
    public async Task Blocking_closes_sessions_stops_login_and_publishes_the_event()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = RolesTests.Access(await RolesTests.LoginAsAdminAsync(host, "admin@example.az"));
        var user = await host.LoginAsync(Email);
        var id = user.GetProperty("userId").GetString();

        using var block = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/users/{id}/block", admin, new { reason = "Saxta hesab" });
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);

        using var refresh = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = user.GetProperty("tokens").GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal("user_blocked", (await host.JsonAsync(refresh)).GetProperty("code").GetString());

        using (var send = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email }))
        {
            Assert.Equal(HttpStatusCode.NoContent, send.StatusCode);
        }

        using var verify = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = host.Otp.LastCode(Email) });
        Assert.Equal("user_blocked", (await host.JsonAsync(verify)).GetProperty("code").GetString());

        var stored = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument("email", Email)).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(stored["isBlocked"].AsBoolean);
        Assert.Equal("Saxta hesab", stored["blockReason"].AsString);
        Assert.Equal(0, await ActiveRefreshTokensAsync(host, stored["_id"]));
        Assert.True(await OutboxHasAsync(host, "UserBlocked"));
        Assert.True(await OutboxHasAsync(host, "\"action\":\"Block\""));
    }

    [Fact]
    public async Task Unblocked_user_can_log_in_again()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = RolesTests.Access(await RolesTests.LoginAsAdminAsync(host, "admin@example.az"));
        var id = (await host.LoginAsync(Email)).GetProperty("userId").GetString();

        using (var block = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/users/{id}/block", admin, new { reason = "Yoxlama" }))
        using (var unblock = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/users/{id}/unblock", admin))
        {
            Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        }

        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(16);
        var login = await host.LoginAsync(Email);

        Assert.Equal(id, login.GetProperty("userId").GetString());
        Assert.True(await OutboxHasAsync(host, "UserUnblocked"));
    }

    [Fact]
    public async Task Only_admins_block_and_never_themselves()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var adminLogin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var user = await host.LoginAsync(Email);

        using var self = await host.SendAsync(
            HttpMethod.Post, $"/api/v1/admin/users/{adminLogin.GetProperty("userId").GetString()}/block", RolesTests.Access(adminLogin), new { reason = "x" });
        using var plain = await host.SendAsync(
            HttpMethod.Post, $"/api/v1/admin/users/{user.GetProperty("userId").GetString()}/block", RolesTests.Access(user), new { reason = "x" });

        Assert.Equal("cannot_block_self", (await host.JsonAsync(self)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
    }

    private static Task<long> ActiveRefreshTokensAsync(ApiTestHost host, BsonValue userId) =>
        host.Database.GetCollection<BsonDocument>("refreshTokens").CountDocumentsAsync(
            new BsonDocument { { "userId", userId }, { "revokedAt", BsonNull.Value } }, cancellationToken: TestContext.Current.CancellationToken);

    private static Task<bool> OutboxHasAsync(ApiTestHost host, string fragment) =>
        host.Database.GetCollection<BsonDocument>("outboxMessages").Find(
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(fragment))),
                Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(fragment)))))
            .AnyAsync(TestContext.Current.CancellationToken);
}
