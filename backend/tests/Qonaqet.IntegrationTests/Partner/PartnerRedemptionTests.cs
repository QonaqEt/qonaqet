using System.Net;
using MongoDB.Bson;
using Qonaqet.Domain.Integration;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Partner API ilə kod yoxlama və təhvil (E13-T04): işçi təhvili ilə eyni qaydalar (qismən təhvil daxil), client yalnız öz bizneslərinə,
/// scope tələb olunur, şəxsi məlumat qaytarılmır.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerRedemptionTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Pos_previews_and_partially_redeems_a_voucher_like_staff_without_personal_data()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "pred");
        var sender = await api.LoginAsync("pred-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(api, sender, GiftTestData.Voucher(shop, 50_00, email: "pred-ana@example.az"));
        var codes = await GiftRedemptionTests.CodesAsync(api, token);
        var (_, client, secret) = await PartnerTestData.ClientAsync(
            api, "pred", [PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite], businessIds: [shop.Id]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using (var preview = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/codes/{codes.Short}?businessId={shop.Id}", access))
        {
            var text = await preview.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var body = await api.JsonAsync(preview);
            Assert.Equal(("Gift", 50_00L, true), (body.GetProperty("kind").GetString(), body.GetProperty("gift").GetProperty("remainingQepik").GetInt64(), body.GetProperty("canRedeem").GetBoolean()));
            foreach (var secretValue in new[] { "Ana", "pred-ana", "pred-sender", UserId(sender).ToString() })
            {
                Assert.DoesNotContain(secretValue, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        using (var redeem = await partner.SendAsync(
            HttpMethod.Post, "/partner/v1/redemptions", access,
            new { businessId = shop.Id, code = codes.Qr, amountQepik = 20_00, externalReference = "CHK-1" }, "pred-1"))
        {
            var body = await api.JsonAsync(redeem);
            Assert.True(redeem.StatusCode == HttpStatusCode.Created, body.GetRawText());
            Assert.Equal("CHK-1", body.GetProperty("externalReference").GetString());
            Assert.Equal(30_00, body.GetProperty("redemption").GetProperty("gift").GetProperty("remainingQepik").GetInt64());
            var redemption = Assert.Single(await GiftRedemptionTests.RedemptionsAsync(api, giftId));
            Assert.Equal(Guid.Parse(body.GetProperty("redemptionId").GetString()!), redemption["_id"].AsGuid);
            Assert.Equal(Guid.Parse(client.GetProperty("id").GetString()!), redemption["staffUserId"].AsGuid);
        }

        using var over = await partner.SendAsync(
            HttpMethod.Post, "/partner/v1/redemptions", access, new { businessId = shop.Id, code = codes.Qr, amountQepik = 30_01 }, "pred-2");
        Assert.Equal("gift_insufficient_balance", (await api.JsonAsync(over)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Client_works_only_for_its_own_businesses_and_needs_the_write_scope()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "pscope");
        var sender = await api.LoginAsync("pscope-sender@example.az");
        var (_, token) = await GiftTestData.DeliveredAsync(api, sender, GiftTestData.Voucher(shop, 10_00, email: "pscope-r@example.az"));
        var codes = await GiftRedemptionTests.CodesAsync(api, token);
        var (_, client, secret) = await PartnerTestData.ClientAsync(
            api, "pscope", [PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite], businessIds: [shop.Id]);
        var clientId = client.GetProperty("clientId").GetString()!;
        var full = await partner.AccessTokenAsync(clientId, secret);
        var readOnly = await partner.AccessTokenAsync(clientId, secret, PartnerScopes.CodesRead);

        var otherBusiness = Guid.CreateVersion7();
        using (var foreignPreview = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/codes/{codes.Short}?businessId={otherBusiness}", full))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreignPreview.StatusCode);
            Assert.Equal("business_not_allowed", (await api.JsonAsync(foreignPreview)).GetProperty("code").GetString());
        }

        using (var foreignRedeem = await partner.SendAsync(
            HttpMethod.Post, "/partner/v1/redemptions", full, new { businessId = otherBusiness, code = codes.Qr, amountQepik = 1_00 }, "pscope-1"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreignRedeem.StatusCode);
        }

        using var noScope = await partner.SendAsync(
            HttpMethod.Post, "/partner/v1/redemptions", readOnly, new { businessId = shop.Id, code = codes.Qr, amountQepik = 1_00 }, "pscope-2");
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
    }
}
