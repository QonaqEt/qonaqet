using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Gifts.Jobs;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Çatdırılma job-u (E09-T05): vaxtında, temalı email + push, alıcının hesabı, idempotentlik.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class GiftDeliveryTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Scheduled_gift_is_delivered_on_time_once_with_a_themed_email_and_pushes()
    {
        fixture.SkipIfUnavailable();
        var push = new RecordingPushSender();
        var emails = new RecordingEmailQueue();
        await using var host = await fixture.HostAsync(configure: s =>
        {
            s.AddSingleton<IPushSender>(push);
            s.AddSingleton<IEmailQueue>(emails);
        });
        var shop = await GiftTestData.ShopAsync(host, "gdeliver");
        var sender = await host.LoginAsync("gdeliver-sender@example.az");
        var recipient = await host.LoginAsync("gdeliver-ana@example.az");
        await DeviceAsync(host, sender, "sender-device");
        await DeviceAsync(host, recipient, "ana-device");

        var started = await GiftTestData.CreateAsync(host, sender, new
        {
            kind = "Items", scope = "Business", businessId = shop.Id, items = new[] { new { catalogItemId = shop.CoffeeId, qty = 2 } },
            recipientName = "Ana", recipientEmail = "GDeliver-Ana@example.az", theme = "Birthday", message = "Ad günün mübarək!",
            senderDisplay = "Anonymous", deliverAt = host.Clock.UtcNow.AddDays(1),
        });
        var giftId = started.GetProperty("giftId").GetString()!;
        await GiftTestData.PayAsync(host, started);

        Assert.Equal(0, await RunAsync(host));
        host.Clock.UtcNow = host.Clock.UtcNow.AddDays(1).AddMinutes(1);
        Assert.Equal(1, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal("Delivered", doc["status"].AsString);
        Assert.Equal(UserId(recipient), doc["recipientUserId"].AsGuid);

        var email = Assert.Single(emails.Of("gift-delivered"));
        Assert.Equal("gdeliver-ana@example.az", email.To);
        Assert.Contains("Anonim dost", email.Subject, StringComparison.Ordinal);
        Assert.Contains("2 × Kofe", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Ad günün mübarək!", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("gdeliver-sender", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("#FFB72B", email.HtmlBody, StringComparison.OrdinalIgnoreCase); // Birthday teması → saffron

        // Emaildəki link həmin hədiyyəni açır; göndərənin ilk linki də eynidir.
        var link = System.Text.RegularExpressions.Regex.Match(email.TextBody, @"https?://\S+/g/(?<t>[A-Za-z0-9_-]{43})").Groups["t"].Value;
        Assert.Equal(link, await GiftTestData.TokenAsync(host, sender, giftId));
        using (var page = await host.GetAsync($"/api/v1/public/gifts/{link}", null))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        Assert.Equal(["Sizə hədiyyə gəldi 🎁"], push.TitlesFor("ana-device"));
        Assert.Contains("Hədiyyəniz çatdırıldı", push.TitlesFor("sender-device"));
        Assert.All(push.Sent, p => Assert.DoesNotContain(link, string.Join(' ', p.Message.Data?.Values ?? []), StringComparison.Ordinal));

        // Hədiyyə alıcının "Alınanlar" siyahısındadır.
        using var received = await host.GetAsync("/api/v1/gifts/received", Access(recipient));
        Assert.Single((await host.JsonAsync(received)).EnumerateArray());
    }

    [Fact]
    public async Task Link_only_gift_is_delivered_without_email_and_the_sender_takes_the_link()
    {
        fixture.SkipIfUnavailable();
        var emails = new RecordingEmailQueue();
        await using var host = await fixture.HostAsync(configure: s => s.AddSingleton<IEmailQueue>(emails));
        var shop = await GiftTestData.ShopAsync(host, "glinkonly");
        var sender = await host.LoginAsync("glinkonly-sender@example.az");
        var started = await GiftTestData.CreateAsync(host, sender, GiftTestData.Voucher(shop));
        var giftId = started.GetProperty("giftId").GetString()!;
        await GiftTestData.PayAsync(host, started);

        Assert.Equal(1, await RunAsync(host));
        Assert.Empty(emails.Of("gift-delivered"));
        var token = await GiftTestData.TokenAsync(host, sender, giftId);
        using var page = await host.GetAsync($"/api/v1/public/gifts/{token}", null);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(MongoDB.Bson.BsonNull.Value, (await GiftTestData.GiftDocAsync(host, giftId))["linkTokenSealed"]);
    }

    private static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<DeliverScheduledGiftsJob>(scope.ServiceProvider).RunAsync(Ct);
    }

    private static async Task DeviceAsync(ApiTestHost host, System.Text.Json.JsonElement login, string device)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", Access(login), new { token = device });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
