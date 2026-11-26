using System.Net;
using System.Text.Json;
using Qonaqet.Domain.Integration;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Plus;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Partner API-nin qalan iki endpoint-i (E13-T12): biznesdəki təhvillərin siyahısı (geri almalar daxil, şəxsi məlumatsız) və brend client-inin
/// kupon kodunu yoxlaması.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerListAndCouponTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Redemption_list_shows_gift_redemptions_and_reversals_without_personal_data()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "plist");
        var sender = await api.LoginAsync("plist-sender@example.az");
        var (_, token) = await GiftTestData.DeliveredAsync(api, sender, GiftTestData.Voucher(shop, 50_00, email: "plist-ana@example.az"));
        var codes = await GiftRedemptionTests.CodesAsync(api, token);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "plist", [PartnerScopes.RedemptionsWrite], businessIds: [shop.Id]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        string redemptionId;
        using (var redeem = await partner.SendAsync(
                   HttpMethod.Post, "/partner/v1/redemptions", access, new { businessId = shop.Id, code = codes.Qr, amountQepik = 20_00 }, "plist-1"))
        {
            var body = await api.JsonAsync(redeem);
            Assert.True(redeem.IsSuccessStatusCode, body.GetRawText());
            redemptionId = body.GetProperty("redemptionId").GetString()!;
        }

        using (var reverse = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/redemptions/{redemptionId}/reverse", access, null, "plist-2"))
        {
            Assert.True(reverse.IsSuccessStatusCode);
        }

        await GiftRedemptionTests.RedeemOkAsync(api, shop, codes.Qr, new { amountQepik = 5_00 });

        using var list = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/redemptions?businessId={shop.Id}", access);
        var page = await api.JsonAsync(list);
        Assert.True(list.IsSuccessStatusCode, page.GetRawText());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, page.GetProperty("total").GetInt64());
        Assert.Equal([-20_00L, 5_00L, 20_00L], items.Select(i => i.GetProperty("valueQepik").GetInt64()).Order());
        Assert.Single(items, i => i.GetProperty("reversesRedemptionId").ValueKind == JsonValueKind.String);
        Assert.Single(items, i => !i.GetProperty("byThisClient").GetBoolean());
        foreach (var secretText in new[] { "plist-ana", "plist-sender", "Ana" })
        {
            Assert.DoesNotContain(secretText, page.GetRawText(), StringComparison.Ordinal);
        }

        using var paged = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/redemptions?businessId={shop.Id}&page=2&pageSize=2", access);
        Assert.Single((await api.JsonAsync(paged)).GetProperty("items").EnumerateArray());

        using var invalid = await partner.SendAsync(
            HttpMethod.Get, $"/partner/v1/redemptions?businessId={shop.Id}&from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z", access);
        Assert.Equal("range_invalid", (await api.JsonAsync(invalid)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Brand_client_validates_a_revealed_coupon_code_once()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var setup = await PlusTestData.BrandAsync(api, "pcoupon");
        var couponId = await PlusTestData.CouponAsync(api, setup);
        await PlusTestData.AddCodesAsync(api, setup, couponId, "PC-1");
        var customer = await api.LoginAsync("pcoupon-customer@example.az");
        string code;
        using (var reveal = await api.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{couponId}/reveal", Access(customer)))
        {
            code = (await api.JsonAsync(reveal)).GetProperty("code").GetString()!;
        }

        var brandAccess = await ClientTokenAsync(api, partner, setup.Admin, "Brand", Guid.Parse(setup.BrandId));
        var otherAccess = await ClientTokenAsync(api, partner, setup.Admin, "Partner", Guid.CreateVersion7());

        using (var notBrand = await partner.SendAsync(HttpMethod.Post, "/partner/v1/coupons/validate", otherAccess, new { couponId, code }, "pc-0"))
        {
            Assert.Equal("brand_client_required", (await api.JsonAsync(notBrand)).GetProperty("code").GetString());
        }

        using (var ok = await partner.SendAsync(HttpMethod.Post, "/partner/v1/coupons/validate", brandAccess, new { couponId, code }, "pc-1"))
        {
            var body = await api.JsonAsync(ok);
            Assert.True(ok.IsSuccessStatusCode, body.GetRawText());
            Assert.Equal(couponId, body.GetProperty("couponId").GetString());
            Assert.DoesNotContain("pcoupon-customer", body.GetRawText(), StringComparison.Ordinal);
        }

        using var again = await partner.SendAsync(HttpMethod.Post, "/partner/v1/coupons/validate", brandAccess, new { couponId, code }, "pc-2");
        Assert.Equal("already_redeemed", (await api.JsonAsync(again)).GetProperty("code").GetString());
    }

    private static async Task<string> ClientTokenAsync(ApiTestHost api, PartnerTestHost partner, JsonElement admin, string ownerType, Guid ownerId)
    {
        using var create = await api.SendAsync(HttpMethod.Post, "/api/v1/admin/integration-clients", Access(admin), new
        {
            name = $"{ownerType} kupon",
            ownerType,
            ownerId,
            businessIds = Array.Empty<Guid>(),
            scopes = new[] { PartnerScopes.CouponsValidate },
            isSandbox = false,
            rateLimitPerMinute = 120,
        });
        var body = await api.JsonAsync(create);
        Assert.True(create.StatusCode == HttpStatusCode.Created, body.GetRawText());
        return await partner.AccessTokenAsync(body.GetProperty("client").GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }
}
