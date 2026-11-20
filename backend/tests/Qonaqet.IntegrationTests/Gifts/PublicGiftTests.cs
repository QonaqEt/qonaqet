using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Link token-ləri və ictimai hədiyyə API-si (E09-T03).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PublicGiftTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Public_page_shows_the_gift_without_login_and_first_open_notifies_the_sender()
    {
        fixture.SkipIfUnavailable();
        var push = new RecordingPushSender();
        await using var host = await fixture.HostAsync(configure: s => s.AddSingleton<IPushSender>(push));
        var shop = await GiftTestData.ShopAsync(host, "gpublic");
        var sender = await host.LoginAsync("gpublic-sender@example.az");
        using (await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", Access(sender), new { token = "sender-device" }))
        {
        }

        var started = await GiftTestData.CreateAsync(host, sender, GiftTestData.Voucher(shop, display: "Anonymous", email: "ana@example.az"));
        var giftId = started.GetProperty("giftId").GetString()!;
        await GiftTestData.PayAsync(host, started);
        var token = await GiftTestData.TokenAsync(host, sender, giftId);

        // Çatdırılmadan əvvəl link heç nə göstərmir.
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync($"/api/v1/public/gifts/{token}", null)));

        await GiftTestData.DeliverNowAsync(host, giftId);
        using var opened = await host.GetAsync($"/api/v1/public/gifts/{token}", null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var text = await opened.Content.ReadAsStringAsync(Ct);
        var page = await host.JsonAsync(opened);
        Assert.Equal("Anonim dost", page.GetProperty("senderName").GetString());
        Assert.Equal("Ana", page.GetProperty("recipientName").GetString());
        Assert.Equal("Opened", page.GetProperty("status").GetString());
        Assert.Equal(50_00, page.GetProperty("remainingQepik").GetInt64());
        Assert.True(page.GetProperty("isRedeemable").GetBoolean());
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}$", page.GetProperty("codes").GetProperty("shortCode").GetString()!);
        Assert.Equal(shop.Id, Assert.Single(page.GetProperty("businesses").EnumerateArray()).GetProperty("id").GetGuid());

        // Şəxsi məlumat yoxdur: email-lər, ID-lər, göndərənin əsl adı.
        Assert.DoesNotContain("ana@example.az", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gpublic-sender", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(UserId(sender).ToString(), text, StringComparison.OrdinalIgnoreCase);

        // İkinci açılış statusu və bildirişi təkrarlamır.
        using (await host.GetAsync($"/api/v1/public/gifts/{token}", null))
        {
        }

        Assert.Equal(["Hədiyyənizə baxıldı 🎉"], push.TitlesFor("sender-device"));
        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal("Opened", doc["status"].AsString);

        // Saxta/qısaldılmış token eyni cavabı alır.
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync($"/api/v1/public/gifts/{token[..^1]}A", null)));
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync("/api/v1/public/gifts/short", null)));
    }

    [Fact]
    public async Task Token_is_stored_only_as_a_hash_and_is_never_logged()
    {
        fixture.SkipIfUnavailable();
        var logs = new FormattedLogCapture();
        await using var host = await fixture.HostAsync(configure: s => s.AddSingleton<ILoggerProvider>(logs));
        var shop = await GiftTestData.ShopAsync(host, "gtoken");
        var sender = await host.LoginAsync("gtoken-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Items(shop));

        using (var page = await host.GetAsync($"/api/v1/public/gifts/{token}", null))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        // Uğursuz sorğu da xəbərdarlıq loglayır (RequestPath scope-u ilə) — token maskalanmalıdır.
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync($"/api/v1/public/gifts/{token[..^2]}AA", null)));
        Assert.Contains(logs.Lines, l => l.Contains("/api/v1/public/gifts/***", StringComparison.Ordinal));

        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.DoesNotContain(token, doc.ToJson(), StringComparison.Ordinal);
        Assert.Equal(BsonNull.Value, doc["linkTokenSealed"]); // link götürülüb, email yoxdur → şifrəli nüsxə də silinib
        Assert.DoesNotContain(logs.Lines, l => l.Contains(token, StringComparison.Ordinal) || l.Contains(token[..^2], StringComparison.Ordinal));

        var outbox = await host.Database.GetCollection<BsonDocument>(Infrastructure.Messaging.Outbox.MongoOutbox.CollectionName).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(Ct);
        Assert.DoesNotContain(outbox, m => m.ToJson().Contains(token, StringComparison.Ordinal));

        // Trace-lərdə yol maskalanır.
        Assert.Equal($"/api/v1/public/gifts/{SensitivePathRedactor.Mask}", SensitivePathRedactor.Redact($"/api/v1/public/gifts/{token}"));
        Assert.Equal($"https://qonaqet.az/g/{SensitivePathRedactor.Mask}?x=1", SensitivePathRedactor.Redact($"https://qonaqet.az/g/{token}?x=1"));
        Assert.Equal("/api/v1/gifts/sent", SensitivePathRedactor.Redact("/api/v1/gifts/sent"));
    }

    [Fact]
    public async Task Link_hash_index_is_partial_unique_and_the_endpoint_is_rate_limited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var indexes = await (await host.Database.GetCollection<BsonDocument>("gifts").Indexes.ListAsync(cancellationToken: Ct)).ToListAsync(Ct);
        var index = indexes.Single(i => i["name"] == "ux_gifts_linkTokenHash");
        Assert.True(index["unique"].AsBoolean);
        Assert.False(index["partialFilterExpression"]["isDeleted"].AsBoolean);
        Assert.Equal("string", index["partialFilterExpression"]["linkTokenHash"]["$type"].AsString);

        var limit = typeof(Api.Controllers.PublicGiftsController).GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal("gift-link", limit?.PolicyName);
    }
}
