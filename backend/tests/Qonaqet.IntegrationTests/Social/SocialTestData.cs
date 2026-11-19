using System.Text.Json;
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
}

internal sealed record Shop(JsonElement Admin, JsonElement Owner, JsonElement Staff, string BusinessId);
