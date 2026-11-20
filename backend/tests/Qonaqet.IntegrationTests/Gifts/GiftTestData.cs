using System.Text.Json;
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
