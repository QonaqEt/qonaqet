using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Users.Common;
using Qonaqet.Domain.Users;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>Bildiriş seçimləri (E03-T05).</summary>
[Collection(MongoCollection.Name)]
public sealed class NotificationPreferenceApiTests(MongoFixture mongo)
{
    [Fact]
    public async Task User_can_turn_off_a_notification_type_and_read_it_back()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync("prefs@example.az"));

        using (var initial = await host.GetAsync("/api/v1/me/notifications", token))
        {
            var items = (await host.JsonAsync(initial)).GetProperty("items");
            Assert.Equal(Enum.GetValues<NotificationType>().Length, items.GetArrayLength());
            Assert.All(items.EnumerateArray(), i => Assert.True(i.GetProperty("push").GetBoolean() && i.GetProperty("email").GetBoolean()));
        }

        using var patch = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/notifications", token,
            new { items = new[] { new { type = "Campaigns", push = false, email = false }, new { type = "Leaderboard", push = false, email = true } } });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        using var after = await host.GetAsync("/api/v1/me/notifications", token);
        var byType = (await host.JsonAsync(after)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("type").GetString()!);
        Assert.False(byType["Campaigns"].GetProperty("push").GetBoolean());
        Assert.False(byType["Campaigns"].GetProperty("email").GetBoolean());
        Assert.False(byType["Leaderboard"].GetProperty("push").GetBoolean());
        Assert.True(byType["Leaderboard"].GetProperty("email").GetBoolean());
        Assert.True(byType["GiftReceived"].GetProperty("push").GetBoolean());
    }

    [Fact]
    public async Task Unknown_or_duplicate_types_are_rejected()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var token = RolesTests.Access(await host.LoginAsync("prefs-bad@example.az"));

        using var duplicate = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/notifications", token,
            new { items = new[] { new { type = "Campaigns", push = false, email = false }, new { type = "Campaigns", push = true, email = true } } });
        using var empty = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/notifications", token, new { items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Push_is_not_sent_for_a_disabled_type()
    {
        mongo.SkipIfUnavailable();
        var push = new CountingPushSender();
        await using var host = new ApiTestHost(mongo, configure: s => s.AddSingleton<IPushSender>(push));
        var login = await host.LoginAsync("prefs-push@example.az");
        var token = RolesTests.Access(login);
        using (await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", token, new { token = "device-1" }))
        {
        }

        using (await host.SendAsync(HttpMethod.Patch, "/api/v1/me/notifications", token, new { items = new[] { new { type = "Campaigns", push = false, email = true } } }))
        {
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<UserPushNotifier>();
            var userId = UserId.From(login.GetProperty("userId").GetGuid());
            await notifier.NotifyAsync(userId, NotificationType.Campaigns, new PushMessage("Novruz", "Kampaniya başladı"), TestContext.Current.CancellationToken);
            await notifier.NotifyAsync(userId, NotificationType.GiftReceived, new PushMessage("Hədiyyə", "Sizə hədiyyə var"), TestContext.Current.CancellationToken);
        }

        Assert.Equal(["Hədiyyə"], push.Titles);
    }

    private sealed class CountingPushSender : IPushSender
    {
        public List<string> Titles { get; } = [];

        public Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
        {
            Titles.Add(message.Title);
            return Task.FromResult(new PushSendResult([]));
        }
    }
}
