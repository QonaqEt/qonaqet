using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Hədiyyənin yaradılması və ödənişi (E09-T02): AwaitingPayment → Scheduled, link token-i bir dəfə, Idempotency-Key.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CreateGiftTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Gift_is_created_awaiting_payment_then_scheduled_with_a_link_revealed_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gcreate");
        var sender = await host.LoginAsync("gcreate-sender@example.az");

        var started = await GiftTestData.CreateAsync(host, sender, new
        {
            kind = "Items", scope = "Business", businessId = shop.Id, items = new[] { new { catalogItemId = shop.CoffeeId, qty = 2 } },
            amountQepik = 1, recipientName = "Ana", recipientEmail = "Ana@Example.az", theme = "Family", message = "Çayını məndən iç",
            senderDisplay = "Named", expiryAction = "RefundToPayer",
        });
        Assert.Equal(2 * 3_50, started.GetProperty("amountQepik").GetInt64());
        var giftId = started.GetProperty("giftId").GetString()!;

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal("AwaitingPayment", doc["status"].AsString);
        Assert.Equal("ana@example.az", doc["recipientEmail"].AsString);
        Assert.Equal("Pickup", doc["fulfillmentType"].AsString);
        Assert.Equal(BsonNull.Value, doc["linkTokenHash"]);
        var payment = await LoadAsync(host, PaymentId.From(Guid.Parse(started.GetProperty("paymentId").GetString()!)));
        Assert.Equal((PaymentPurpose.Gift, 7_00L), (payment.Purpose, payment.Amount));
        Assert.DoesNotContain("Ana", payment.Description, StringComparison.Ordinal);

        // Ödənişdən əvvəl link yoxdur.
        Assert.Equal("gift_link_unavailable", await GiftTestData.ErrorCodeAsync(await GiftTestData.ShareLinkAsync(host, sender, giftId)));

        await GiftTestData.PayAsync(host, started);
        doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal("Scheduled", doc["status"].AsString);
        Assert.True(doc["linkTokenHash"].IsString);
        Assert.True(doc["linkTokenSealed"].IsString);

        // Təkrar çatdırılan hadisə ikinci token yaratmır.
        await DeliverAsync(host, Fulfillment(host));
        Assert.Equal(doc["linkTokenHash"], (await GiftTestData.GiftDocAsync(host, giftId))["linkTokenHash"]);

        using var link = await GiftTestData.ShareLinkAsync(host, sender, giftId);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var url = new Uri((await host.JsonAsync(link)).GetProperty("shareUrl").GetString()!);
        Assert.StartsWith("/g/", url.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(43, url.AbsolutePath.Length - 3);

        Assert.Equal("gift_link_already_revealed", await GiftTestData.ErrorCodeAsync(await GiftTestData.ShareLinkAsync(host, sender, giftId)));
        var stranger = await host.LoginAsync("gcreate-stranger@example.az");
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await GiftTestData.ShareLinkAsync(host, stranger, giftId)));
    }

    [Fact]
    public async Task Idempotency_key_is_required_and_replays_the_same_gift()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gidem");
        var sender = Access(await host.LoginAsync("gidem-sender@example.az"));
        var body = new
        {
            kind = "Voucher", scope = "Business", businessId = shop.Id, amountQepik = 20_00, recipientName = "Dost", theme = "Thanks",
            senderDisplay = "Anonymous",
        };

        using (var noKey = await host.SendAsync(HttpMethod.Post, "/api/v1/gifts", sender, body))
        {
            Assert.Equal("idempotency_key_required", await GiftTestData.ErrorCodeAsync(noKey));
        }

        using var first = await PostIdempotentAsync(host, "/api/v1/gifts", sender, body, "gift-key-1");
        using var second = await PostIdempotentAsync(host, "/api/v1/gifts", sender, body, "gift-key-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal((await host.JsonAsync(first)).GetProperty("giftId").GetString(), (await host.JsonAsync(second)).GetProperty("giftId").GetString());
        Assert.Equal(1, await host.Database.GetCollection<BsonDocument>("gifts").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Invalid_requests_are_rejected_and_unpaid_gifts_are_cancelled()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "ginvalid");
        var sender = await host.LoginAsync("ginvalid-sender@example.az");
        object Body(string? name = "Dost", string? message = null, DateTimeOffset? deliverAt = null, string? email = null, string theme = "Love") => new
        {
            kind = "Voucher", scope = "Network", amountQepik = 10_00, recipientName = name, recipientEmail = email, theme, message,
            senderDisplay = "Named", deliverAt,
        };

        Assert.Equal("gift_recipient_required", await GiftTestData.CreateErrorAsync(host, sender, Body(name: " ")));
        Assert.Equal("too_long", await GiftTestData.CreateErrorAsync(host, sender, Body(message: new string('a', 501))));
        Assert.Equal("invalid_email", await GiftTestData.CreateErrorAsync(host, sender, Body(email: "not-an-email")));
        Assert.Equal("invalid_theme", await GiftTestData.CreateErrorAsync(host, sender, Body(theme: "Corporate")));
        Assert.Equal("gift_deliver_at_out_of_range", await GiftTestData.CreateErrorAsync(host, sender, Body(deliverAt: host.Clock.UtcNow.AddDays(91))));

        var scheduled = await GiftTestData.CreateAsync(host, sender, Body(deliverAt: host.Clock.UtcNow.AddDays(90)));
        var doc = await GiftTestData.GiftDocAsync(host, scheduled.GetProperty("giftId").GetString()!);
        Assert.Equal("Network", doc["scope"].AsString);
        Assert.Equal(BsonNull.Value, doc["businessId"]);
        Assert.Equal(host.Clock.UtcNow.AddDays(150).UtcDateTime, doc["expiresAt"].ToUniversalTime(), TimeSpan.FromSeconds(1));

        await PayAsync(host, scheduled.GetProperty("paymentUrl").GetString()!, paid: false);
        await DeliverAsync(host, Fulfillment(host));
        Assert.Equal("Cancelled", (await GiftTestData.GiftDocAsync(host, scheduled.GetProperty("giftId").GetString()!))["status"].AsString);
    }

    [Fact]
    public async Task Donate_on_expiry_targets_the_configured_default_pool()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = await host.LoginAsync("gdonate-sender@example.az");
        var started = await GiftTestData.CreateAsync(host, sender, new
        {
            kind = "Voucher", scope = "Network", amountQepik = 10_00, recipientName = "Dost", theme = "JustBecause", senderDisplay = "Named",
            expiryAction = "DonateToPool",
        });

        var doc = await GiftTestData.GiftDocAsync(host, started.GetProperty("giftId").GetString()!);
        var everyone = await Social.SocialTestData.CategoryAsync(host, "everyone");
        var pool = await Donations.PoolDonationTests.DefaultPoolAsync(host, everyone);
        Assert.Equal("DonateToPool", doc["expiryAction"].AsString);
        Assert.Equal(pool["_id"], doc["expiryPoolId"]);
    }
}
