using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Users.Common;
using Qonaqet.Domain.Users;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>FCM token-lərinin idarəsi (E02-T05).</summary>
[Collection(MongoCollection.Name)]
public sealed class FcmTokenTests(MongoFixture mongo)
{
    private const string Email = "fcm@example.az";

    [Fact]
    public async Task Registering_the_same_token_twice_keeps_one_copy()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync(Email));

        foreach (var device in new[] { "device-a", "device-b", "device-a" })
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", token, new { token = device });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(["device-b", "device-a"], await StoredTokensAsync(host));
    }

    [Fact]
    public async Task Only_the_newest_ten_devices_are_kept_and_logout_removes_a_device()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync(Email));

        for (var i = 0; i < User.MaxFcmTokens + 2; i++)
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", token, new { token = $"device-{i}" });
        }

        using var removed = await host.SendAsync(HttpMethod.Delete, "/api/v1/me/fcm-token", token, new { token = "device-11" });
        using var again = await host.SendAsync(HttpMethod.Delete, "/api/v1/me/fcm-token", token, new { token = "device-11" });

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(Enumerable.Range(2, 9).Select(i => $"device-{i}"), await StoredTokensAsync(host));
    }

    [Fact]
    public async Task Invalid_tokens_reported_by_the_push_provider_are_removed_on_send()
    {
        mongo.SkipIfUnavailable();
        var push = new FakePushSender("device-dead");
        await using var host = new ApiTestHost(mongo, configure: s => s.AddSingleton<IPushSender>(push));
        var login = await host.LoginAsync(Email);
        var token = RolesTests.Access(login);
        foreach (var device in new[] { "device-dead", "device-live" })
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", token, new { token = device });
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<UserPushNotifier>();
            await notifier.NotifyAsync(UserId.From(login.GetProperty("userId").GetGuid()), NotificationType.GiftReceived, new PushMessage("Salam", "Hədiyyəniz var"), TestContext.Current.CancellationToken);
        }

        Assert.Equal(["device-dead", "device-live"], push.LastTokens);
        Assert.Equal(["device-live"], await StoredTokensAsync(host));
    }

    private static async Task<IReadOnlyList<string>> StoredTokensAsync(ApiTestHost host)
    {
        var user = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        return user["fcmTokens"].AsBsonArray.Select(t => t.AsString).ToArray();
    }

    private sealed class FakePushSender(params string[] invalid) : IPushSender
    {
        public IReadOnlyList<string> LastTokens { get; private set; } = [];

        public Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
        {
            LastTokens = tokens;
            return Task.FromResult(new PushSendResult([.. tokens.Where(invalid.Contains)]));
        }
    }
}
