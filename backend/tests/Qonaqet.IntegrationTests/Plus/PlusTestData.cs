using System.Net;
using System.Text.Json;
using Qonaqet.IntegrationTests.Businesses;
using Qonaqet.IntegrationTests.Identity;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Plus;

internal sealed record PlusSetup(JsonElement Admin, string BrandId);

/// <summary>QONAQET Plus testləri üçün: admin, brend və kuponlar API üzərindən yaradılır.</summary>
internal static class PlusTestData
{
    public static async Task<PlusSetup> BrandAsync(ApiTestHost host, string prefix, string name = "Bravo")
    {
        var admin = await RolesTests.LoginAsAdminAsync(host, $"{prefix}-admin@example.az");
        using var create = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/brands", Access(admin),
            new { name, website = "https://bravo.az", contactEmail = $"plus@{prefix}.az" });
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        return new PlusSetup(admin, (await host.JsonAsync(create)).GetProperty("id").GetString()!);
    }

    /// <summary>Brend istifadəçisi: admin bağlayır, rol yeni token-ə düşsün deyə token yenilənir.</summary>
    public static async Task<JsonElement> BrandUserAsync(ApiTestHost host, PlusSetup setup, string email)
    {
        var user = await host.LoginAsync(email);
        using (var assign = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/brands/{setup.BrandId}/users", Access(setup.Admin), new { email }))
        {
            Assert.True(assign.IsSuccessStatusCode, await assign.Content.ReadAsStringAsync(Ct));
        }

        return await BusinessTestData.RefreshAsync(host, user);
    }

    public static async Task<string> CouponAsync(
        ApiTestHost host,
        PlusSetup setup,
        string codeType = "Unique",
        string? sharedCode = null,
        int perUserLimit = 1,
        Guid[]? categories = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
    {
        using var create = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/coupons", Access(setup.Admin), new
        {
            brandId = setup.BrandId,
            codeType,
            sharedCode,
            title = "Kofeyə 20% endirim",
            eligibleCategoryIds = categories ?? [],
            perUserLimit,
            validFrom = validFrom ?? host.Clock.UtcNow.AddDays(-1),
            validUntil = validUntil ?? host.Clock.UtcNow.AddDays(30),
        });
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        return (await host.JsonAsync(create)).GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> AddCodesAsync(ApiTestHost host, PlusSetup setup, string couponId, params string[] codes)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/coupons/{couponId}/codes", Access(setup.Admin), new { codes });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }
}
