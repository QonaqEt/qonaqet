using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>Biznes testlərinin ortaq addımları: müraciət, admin aktivləşdirmə, yeni token.</summary>
internal static class BusinessTestData
{
    public const string Iban = "AZ21NABZ00000000137010001944";

    public static object ApplicationBody(string name = "Çay Evi", string voen = "1234567890", double lat = 40.4093, double lng = 49.8671, string type = "Cafe") => new
    {
        name,
        type,
        voen,
        address = "Bakı, Nizami küç. 10",
        latitude = lat,
        longitude = lng,
        phone = "+994 50 123 45 67",
        email = "info@cay.az",
        iban = Iban,
        workingHours = new[] { new { day = "Monday", opens = "09:00", closes = "22:00" } },
    };

    public static string Access(JsonElement login) => login.GetProperty("tokens").GetProperty("accessToken").GetString()!;

    public static async Task<JsonElement> ApplyAsync(ApiTestHost host, JsonElement applicant, object? body = null)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/businesses/apply", Access(applicant), body ?? ApplicationBody());
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await host.JsonAsync(response);
    }

    public static async Task ActivateAsync(ApiTestHost host, JsonElement admin, string businessId)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/businesses/{businessId}/activate", Access(admin));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Müraciət + aktivləşdirmə; sahibin yeni (rolları olan) giriş cavabını və biznes ID-sini qaytarır.</summary>
    public static async Task<(JsonElement Owner, string BusinessId)> ActiveBusinessAsync(
        ApiTestHost host, JsonElement admin, string ownerEmail, object? application = null)
    {
        var owner = await host.LoginAsync(ownerEmail);
        var business = await ApplyAsync(host, owner, application);
        var id = business.GetProperty("id").GetString()!;
        await ActivateAsync(host, admin, id);
        return (await RefreshAsync(host, owner), id);
    }

    /// <summary>Rolları yenilənmiş token üçün refresh.</summary>
    public static async Task<JsonElement> RefreshAsync(ApiTestHost host, JsonElement login)
    {
        using var refreshed = await host.PostAsync("/api/v1/auth/refresh", new { refreshToken = login.GetProperty("tokens").GetProperty("refreshToken").GetString() });
        Assert.True(refreshed.IsSuccessStatusCode);
        var tokens = await host.JsonAsync(refreshed);
        return JsonSerializer.SerializeToElement(new { userId = login.GetProperty("userId").GetString(), tokens });
    }

    public static Task<bool> AuditExistsAsync(ApiTestHost host, string action) =>
        host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression(action))).AnyAsync(TestContext.Current.CancellationToken);

    /// <summary>Outbox-da bu tipdə domen hadisəsi varmı (tip adı üzrə).</summary>
    public static Task<bool> EventExistsAsync(ApiTestHost host, string eventType) =>
        host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression(eventType))).AnyAsync(TestContext.Current.CancellationToken);
}
