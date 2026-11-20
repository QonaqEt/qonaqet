using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Businesses;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Verification;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Sosial qonaqlıq testlərinin ortaq addımları: aktiv biznes, işçi, təkliflər.</summary>
internal static class SocialTestData
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string Access(JsonElement login) => BusinessTestData.Access(login);

    public static string UserId(JsonElement login) => login.GetProperty("userId").GetString()!;

    /// <summary>Aktiv biznes: sahibin (yenilənmiş) girişi, biznes ID-si, işçinin girişi və adminin girişi.</summary>
    public static async Task<Shop> ShopAsync(ApiTestHost host, string prefix = "shop")
    {
        var admin = await RolesTests.LoginAsAdminAsync(host, $"{prefix}-admin@example.az");
        var (owner, id) = await BusinessTestData.ActiveBusinessAsync(host, admin, $"{prefix}-owner@example.az");
        var staff = await host.LoginAsync($"{prefix}-barista@example.az");
        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = $"{prefix}-barista@example.az" }))
        {
            Assert.True(add.IsSuccessStatusCode, await add.Content.ReadAsStringAsync(Ct));
        }

        return new Shop(admin, owner, await BusinessTestData.RefreshAsync(host, staff), id);
    }

    public static Task<string> CategoryAsync(ApiTestHost host, string code) => VerificationTestData.CategoryIdAsync(host, code);

    public static async Task<JsonElement> DailyAsync(ApiTestHost host, Shop shop, string categoryId, int quota = 3, string title = "Çay", object? extra = null)
    {
        object body = extra ?? new { categoryId, title, dailyQuota = quota, donorDisplay = "Named" };
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/offers", Access(shop.Owner), body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    public static async Task<JsonElement> SuspendedAsync(
        ApiTestHost host, Shop shop, string categoryId, int quantity = 2, string? donorEmail = null, string display = "Anonymous", string? donorName = null)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/offers/suspended", Access(shop.Staff),
            new { categoryId, title = "Kofe", quantity, donorDisplay = display, donorName, donorEmail });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    public static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    public static BsonBinaryData Bin(string id) => new(Guid.Parse(id), GuidRepresentation.Standard);

    /// <summary>İstifadəçiyə kateqoriya təsdiqi yazır (birbaşa DB-də; admin axını E05 testlərindədir).</summary>
    public static Task VerifyAsync(ApiTestHost host, string email, string categoryId, DateTimeOffset? validUntil = null) =>
        host.Database.GetCollection<BsonDocument>("users").UpdateOneAsync(
            new BsonDocument("email", email),
            new BsonDocument("$push", new BsonDocument("verifiedCategories", new BsonDocument
            {
                { "categoryId", Bin(categoryId) },
                { "validUntil", (validUntil ?? host.Clock.UtcNow.AddDays(30)).UtcDateTime },
                { "verificationId", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) },
                { "verifiedAt", DateTime.UtcNow },
            })),
            cancellationToken: Ct);

    public static Task<HttpResponseMessage> ClaimAsync(ApiTestHost host, JsonElement login, string offerId) =>
        host.SendAsync(HttpMethod.Post, $"/api/v1/offers/{offerId}/claim", Access(login));

    /// <summary>Uğurlu claim; cavabın JSON-u.</summary>
    public static async Task<JsonElement> ClaimOkAsync(ApiTestHost host, JsonElement login, string offerId)
    {
        using var response = await ClaimAsync(host, login, offerId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    public static async Task<string?> CodeAsync(ApiTestHost host, HttpResponseMessage response) =>
        (await host.JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    public static async Task<BsonDocument> OfferDocAsync(ApiTestHost host, string offerId) =>
        await host.Database.GetCollection<BsonDocument>("offers").Find(new BsonDocument("_id", Bin(offerId))).SingleAsync(Ct);
}

internal sealed record Shop(JsonElement Admin, JsonElement Owner, JsonElement Staff, string BusinessId);

/// <summary>Bütün log sətirlərini tutur (kodların loglanmadığını yoxlamaq üçün).</summary>
internal sealed class LogCapture : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(_lines);

    public void Dispose()
    {
    }

    private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> lines) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            lines.Enqueue(state.ToString() ?? string.Empty);
            return null;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            lines.Enqueue(formatter(state, exception) + " " + state + " " + exception);
        }
    }
}
