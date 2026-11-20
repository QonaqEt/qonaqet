using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Hədiyyə testlərinin ortaq hazırlığı: aktiv biznes, işçi, kataloq (hədiyyə edilə bilən və gizli məhsul).</summary>
internal static class GiftTestData
{
    public static async Task<GiftShop> ShopAsync(ApiTestHost host, string prefix, bool acceptsNetwork = false)
    {
        var shop = await SocialTestData.ShopAsync(host, prefix);
        if (acceptsNetwork)
        {
            using var settings = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{shop.BusinessId}/settings", Access(shop.Owner),
                new { acceptsNetworkVouchers = true });
            Assert.True(settings.IsSuccessStatusCode, await settings.Content.ReadAsStringAsync(Ct));
        }

        var coffee = await ItemAsync(host, shop, "Kofe", 3_50, giftable: true);
        var cake = await ItemAsync(host, shop, "Paxlava", 5_00, giftable: true);
        var hidden = await ItemAsync(host, shop, "Daxili", 1_00, giftable: false);
        return new GiftShop(shop, Guid.Parse(shop.BusinessId), coffee, cake, hidden);
    }

    /// <summary><c>POST gifts</c> (Idempotency-Key ilə); 201 gözlənilir.</summary>
    public static async Task<JsonElement> CreateAsync(ApiTestHost host, JsonElement sender, object body)
    {
        using var response = await PostIdempotentAsync(host, "/api/v1/gifts", Access(sender), body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    public static async Task<string?> CreateErrorAsync(ApiTestHost host, JsonElement sender, object body) =>
        await ErrorCodeAsync(await PostIdempotentAsync(host, "/api/v1/gifts", Access(sender), body));

    /// <summary>Xətanın sabit kodu (validasiyada ilk sahənin kodu); uğurlu cavabda <c>null</c>. Cavabı bağlayır.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
            var code = root.GetProperty("code").GetString();
            return code == "validation_failed" ? root.GetProperty("errors")[0].GetProperty("code").GetString() : code;
        }
    }

    /// <summary>Fake kassada ödəyir və ödəniş təsdiqi consumer-ini işlədir (hədiyyə Scheduled olur).</summary>
    public static async Task PayAsync(ApiTestHost host, JsonElement started)
    {
        await Payments.PaymentTestData.PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        await DeliverAsync(host, Fulfillment(host));
    }

    public static Task<HttpResponseMessage> ShareLinkAsync(ApiTestHost host, JsonElement sender, string giftId) =>
        host.SendAsync(HttpMethod.Post, $"/api/v1/gifts/{giftId}/share-link", Access(sender), new { });

    /// <summary>Paylaşma linkindən token (yalnız testdə; real axında heç yerdə saxlanılmır).</summary>
    public static async Task<string> TokenAsync(ApiTestHost host, JsonElement sender, string giftId)
    {
        using var response = await ShareLinkAsync(host, sender, giftId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return new Uri((await host.JsonAsync(response)).GetProperty("shareUrl").GetString()!).AbsolutePath["/g/".Length..];
    }

    /// <summary>Çatdırılma job-unun edəcəyi kimi hədiyyəni çatdırır (job-un öz testləri ayrıdır).</summary>
    public static async Task DeliverNowAsync(ApiTestHost host, string giftId, Guid? recipientUserId = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gifts = scope.ServiceProvider.GetRequiredService<IGiftRepository>();
        var gift = (await gifts.GetAsync(GiftId.From(Guid.Parse(giftId)), Ct))!;
        var version = gift.Version;
        gift.Deliver(recipientUserId, host.Clock.UtcNow);
        Assert.True(await gifts.TryReplaceAsync(gift, version, Ct));
    }

    /// <summary>Yaradır, ödəyir, linki götürür və çatdırır: alıcının əlində olan hədiyyə və onun token-i.</summary>
    public static async Task<(string GiftId, string Token)> DeliveredAsync(ApiTestHost host, JsonElement sender, object body)
    {
        var started = await CreateAsync(host, sender, body);
        var giftId = started.GetProperty("giftId").GetString()!;
        await PayAsync(host, started);
        var token = await TokenAsync(host, sender, giftId);
        await DeliverNowAsync(host, giftId);
        return (giftId, token);
    }

    public static object Voucher(GiftShop shop, long amount = 50_00, string display = "Named", string? email = null) => new
    {
        kind = "Voucher", scope = "Business", businessId = shop.Id, amountQepik = amount, recipientName = "Ana", recipientEmail = email,
        theme = "Family", message = "Səni sevirəm", senderDisplay = display,
    };

    public static object Items(GiftShop shop, int coffees = 2, int cakes = 1) => new
    {
        kind = "Items", scope = "Business", businessId = shop.Id,
        items = new[] { new { catalogItemId = shop.CoffeeId, qty = coffees }, new { catalogItemId = shop.CakeId, qty = cakes } },
        recipientName = "Ata", theme = "Thanks", senderDisplay = "Named",
    };

    public static Task<BsonDocument> GiftDocAsync(ApiTestHost host, string giftId) =>
        host.Database.GetCollection<BsonDocument>("gifts")
            .Find(new BsonDocument("_id", new BsonBinaryData(Guid.Parse(giftId), GuidRepresentation.Standard))).SingleAsync(Ct);

    private static async Task<Guid> ItemAsync(ApiTestHost host, Shop shop, string title, long price, bool giftable)
    {
        using var item = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/catalog", Access(shop.Owner),
            new { title, priceQepik = price, isActive = true, isGiftable = giftable });
        Assert.True(item.IsSuccessStatusCode, await item.Content.ReadAsStringAsync(Ct));
        return Guid.Parse((await host.JsonAsync(item)).GetProperty("id").GetString()!);
    }
}

internal sealed record GiftShop(Shop Shop, Guid Id, Guid CoffeeId, Guid CakeId, Guid HiddenId)
{
    public JsonElement Owner => Shop.Owner;

    public JsonElement Staff => Shop.Staff;
}

/// <summary>Push-ları yaddaşda saxlayır (kimə, nə).</summary>
internal sealed class RecordingPushSender : IPushSender
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(IReadOnlyList<string> Tokens, PushMessage Message)> _sent = new();

    public IReadOnlyCollection<(IReadOnlyList<string> Tokens, PushMessage Message)> Sent => _sent;

    public IReadOnlyList<string> TitlesFor(string device) => [.. _sent.Where(s => s.Tokens.Contains(device)).Select(s => s.Message.Title)];

    public Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue((tokens, message));
        return Task.FromResult(new PushSendResult([]));
    }
}

/// <summary>
/// Logları real JSON formatter-dən (<see cref="Infrastructure.Observability.RedactingJsonConsoleFormatter"/>) keçirərək tutur — scope-lar daxil,
/// yəni production çıxışında nə yazılacaqsa onu.
/// </summary>
internal sealed class FormattedLogCapture : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ISupportExternalScope
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();
    private readonly Infrastructure.Observability.RedactingJsonConsoleFormatter _formatter = new();
    private Microsoft.Extensions.Logging.IExternalScopeProvider? _scopes;

    public IReadOnlyCollection<string> Lines => _lines;

    public void SetScopeProvider(Microsoft.Extensions.Logging.IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(FormattedLogCapture owner, string category) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes?.Push(state);

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            using var writer = new StringWriter();
            owner._formatter.Write(
                new Microsoft.Extensions.Logging.Abstractions.LogEntry<TState>(logLevel, category, eventId, state, exception, formatter), owner._scopes, writer);
            owner._lines.Enqueue(writer.ToString());
        }
    }
}
