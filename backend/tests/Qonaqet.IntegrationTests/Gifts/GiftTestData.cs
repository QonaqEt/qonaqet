using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
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
