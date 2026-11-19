using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>Profil: GET/PATCH/DELETE me (E02-T04).</summary>
[Collection(MongoCollection.Name)]
public sealed class ProfileTests(MongoFixture mongo)
{
    private const string Email = "nigar@example.az";

    [Fact]
    public async Task Registration_name_step_and_profile_updates()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync(Email));
        var avatar = Guid.CreateVersion7();

        using var before = await host.GetAsync("/api/v1/me", token);
        Assert.False((await host.JsonAsync(before)).GetProperty("profileCompleted").GetBoolean());

        using var named = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", token, new { firstName = " Nigar ", lastName = "Əliyeva", avatarFileId = avatar });
        var me = await host.JsonAsync(named);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal("Nigar", me.GetProperty("firstName").GetString());
        Assert.Equal("Əliyeva", me.GetProperty("lastName").GetString());
        Assert.Equal(avatar, me.GetProperty("avatarFileId").GetGuid());
        Assert.True(me.GetProperty("profileCompleted").GetBoolean());
        Assert.Equal(Email, me.GetProperty("email").GetString());

        using var cleared = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", token, new { lastName = "", removeAvatar = true });
        var after = await host.JsonAsync(cleared);
        Assert.Equal("Nigar", after.GetProperty("firstName").GetString());
        Assert.Equal(JsonValueKindNull, after.GetProperty("lastName").ValueKind);
        Assert.Equal(JsonValueKindNull, after.GetProperty("avatarFileId").ValueKind);

        using var fresh = await host.GetAsync("/api/v1/me", token);
        Assert.Equal("Nigar", (await host.JsonAsync(fresh)).GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Name_cannot_be_blank_and_profile_needs_authentication()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync(Email));

        using var blank = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", token, new { firstName = "   " });
        using var noName = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", token, new { lastName = "Əliyeva" });
        using var anonymous = await host.GetAsync("/api/v1/me", null);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("first_name_required", (await host.JsonAsync(noName)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_soft_deletes_it_closes_sessions_and_is_audited()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var login = await host.LoginAsync(Email);
        var token = RolesTests.Access(login);

        using var delete = await host.SendAsync(HttpMethod.Delete, "/api/v1/me?reason=test", token);
        using var me = await host.GetAsync("/api/v1/me", token);
        using var refresh = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = login.GetProperty("tokens").GetProperty("refreshToken").GetString() });

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        var user = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(user["isDeleted"].AsBoolean);
        Assert.Equal("test", user["deleteReason"].AsString);
        var token0 = await host.Database.GetCollection<BsonDocument>("refreshTokens").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("AccountDeleted", token0["revokeReason"].AsString);
        var audit = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression("\"action\":\"SoftDelete\""))).AnyAsync(TestContext.Current.CancellationToken);
        Assert.True(audit);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;
}
