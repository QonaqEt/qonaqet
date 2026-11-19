using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>Rollar və pipeline siyasətləri (E02-T03).</summary>
[Collection(MongoCollection.Name)]
public sealed class RolesTests(MongoFixture mongo)
{
    [Fact]
    public async Task Admin_endpoint_rejects_anonymous_with_401_and_plain_users_with_403()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var user = await host.LoginAsync("user@example.az");
        var target = user.GetProperty("userId").GetString();

        using var anonymous = await PutRolesAsync(host, null, target, ["Admin"]);
        using var plain = await PutRolesAsync(host, Access(user), target, ["Admin"]);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("unauthorized", (await host.JsonAsync(anonymous)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, plain.StatusCode);
        Assert.Equal("forbidden", (await host.JsonAsync(plain)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_assigns_roles_and_they_reach_the_token_after_refresh()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await LoginAsAdminAsync(host, "admin@example.az");
        var owner = await host.LoginAsync("owner@example.az");

        using var response = await PutRolesAsync(host, Access(admin), owner.GetProperty("userId").GetString(), ["BusinessOwner"]);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var refreshed = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = owner.GetProperty("tokens").GetProperty("refreshToken").GetString() });
        using var whoami = await host.GetAsync("/__test/auth/whoami", (await host.JsonAsync(refreshed)).GetProperty("accessToken").GetString());
        var roles = (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(["User", "BusinessOwner"], roles);

        var audit = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression("SetRoles"))).AnyAsync(TestContext.Current.CancellationToken);
        Assert.True(audit);
    }

    [Fact]
    public async Task Admin_cannot_remove_their_own_admin_role_and_unknown_roles_fail_validation()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await LoginAsAdminAsync(host, "admin@example.az");
        var self = admin.GetProperty("userId").GetString();

        using var demote = await PutRolesAsync(host, Access(admin), self, ["User"]);
        using var unknown = await PutRolesAsync(host, Access(admin), self, ["Wizard"]);
        using var missing = await PutRolesAsync(host, Access(admin), Guid.CreateVersion7().ToString(), ["Admin"]);

        Assert.Equal("cannot_remove_own_admin", (await host.JsonAsync(demote)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("user_not_found", (await host.JsonAsync(missing)).GetProperty("code").GetString());
    }

    /// <summary>Hesabı Mongo-da birbaşa admin edir (seed kimi) və yeni token alır.</summary>
    internal static async Task<JsonElement> LoginAsAdminAsync(ApiTestHost host, string email)
    {
        await host.LoginAsync(email);
        await host.Database.GetCollection<BsonDocument>("users").UpdateOneAsync(
            new BsonDocument("email", email),
            new BsonDocument("$set", new BsonDocument("roles", new BsonArray { "User", "Admin" })),
            cancellationToken: TestContext.Current.CancellationToken);
        return await host.LoginAsync(email);
    }

    internal static string? Access(JsonElement login) => login.GetProperty("tokens").GetProperty("accessToken").GetString();

    private static async Task<HttpResponseMessage> PutRolesAsync(ApiTestHost host, string? accessToken, string? userId, string[] roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/admin/users/{userId}/roles", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roles }),
        };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
