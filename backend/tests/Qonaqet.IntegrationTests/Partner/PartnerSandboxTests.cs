using System.Net;
using System.Text.Json;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Sandbox rejimi (E13-T09): sandbox client yalnız sandbox biznesdə işləyir və test kodları alır; sandbox kodu real biznesdə, real client
/// sandbox biznesdə işləmir; sandbox biznesi axtarışda görünmür; sandbox təhvili ledger-ə yazılmır.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerSandboxTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Sandbox_client_redeems_test_codes_only_at_sandbox_businesses()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var sandboxShop = await GiftTestData.ShopAsync(api, "psb-test", acceptsNetwork: true);
        var liveShop = await GiftTestData.ShopAsync(api, "psb-live");
        var scopes = new[] { PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite };

        // Biznes sonradan sandbox olur: ona əvvəl bağlanmış real client artıq çıxa bilmir.
        var (_, early, earlySecret) = await PartnerTestData.ClientAsync(api, "psb-e", scopes, businessIds: [sandboxShop.Id]);
        await SetSandboxAsync(api, sandboxShop);

        var (_, sandboxClient, sandboxSecret) = await PartnerTestData.ClientAsync(api, "psb", scopes, businessIds: [sandboxShop.Id], sandbox: true);
        var (_, liveClient, liveSecret) = await PartnerTestData.ClientAsync(api, "psb-l", scopes, businessIds: [liveShop.Id]);
        var sandbox = await partner.AccessTokenAsync(sandboxClient.GetProperty("clientId").GetString()!, sandboxSecret);
        var live = await partner.AccessTokenAsync(liveClient.GetProperty("clientId").GetString()!, liveSecret);

        // Real client test kodu ala bilməz; sandbox client yalnız öz sandbox biznesi üçün alır.
        using (var forbidden = await partner.SendAsync(HttpMethod.Post, "/partner/v1/sandbox/test-codes", live, new { businessId = liveShop.Id }, "psb-0"))
        {
            Assert.Equal("sandbox_only", (await api.JsonAsync(forbidden)).GetProperty("code").GetString());
        }

        JsonElement code;
        using (var created = await partner.SendAsync(
                   HttpMethod.Post, "/partner/v1/sandbox/test-codes", sandbox, new { businessId = sandboxShop.Id, amountQepik = 30_00 }, "psb-1"))
        {
            code = await api.JsonAsync(created);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(30_00, code.GetProperty("amountQepik").GetInt64());
        }

        var shortCode = code.GetProperty("shortCode").GetString()!;
        using (var preview = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/codes/{shortCode}?businessId={sandboxShop.Id}", sandbox))
        {
            Assert.True(preview.IsSuccessStatusCode, (await api.JsonAsync(preview)).GetRawText());
        }

        using (var redeem = await partner.SendAsync(
                   HttpMethod.Post, "/partner/v1/redemptions", sandbox, new { businessId = sandboxShop.Id, code = shortCode, amountQepik = 10_00 }, "psb-2"))
        {
            var body = await api.JsonAsync(redeem);
            Assert.True(redeem.IsSuccessStatusCode, body.GetRawText());
        }

        // Sandbox test kodu başqa (real) biznesdə ümumiyyətlə tapılmır; real şəbəkə vauçeri isə sandbox biznesdə keçmir.
        Assert.Equal("code_not_found", await GiftRedemptionTests.RedeemErrorAsync(api, liveShop, shortCode, new { amountQepik = 1_00 }));
        var buyer = await api.LoginAsync("psb-buyer@example.az");
        var (_, liveToken) = await GiftTestData.DeliveredAsync(api, buyer, new
        {
            kind = "Voucher", scope = "Network", amountQepik = 10_00, recipientName = "Ana", theme = "Family", senderDisplay = "Named",
        });
        var liveCodes = await GiftRedemptionTests.CodesAsync(api, liveToken);
        Assert.Equal("sandbox_mismatch", await GiftRedemptionTests.RedeemErrorAsync(api, sandboxShop, liveCodes.Qr, new { amountQepik = 1_00 }));

        // Real client sandbox biznesə çıxa bilmir (business_ids-də olsa belə).
        var earlyAccess = await partner.AccessTokenAsync(early.GetProperty("clientId").GetString()!, earlySecret);
        using var mismatch = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/codes/{shortCode}?businessId={sandboxShop.Id}", earlyAccess);
        Assert.Equal("sandbox_mismatch", (await api.JsonAsync(mismatch)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sandbox_client_cannot_be_bound_to_a_live_business()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        var liveShop = await GiftTestData.ShopAsync(api, "psb-bind");
        var admin = await Identity.RolesTests.LoginAsAdminAsync(api, "psb-bind-partner-admin@example.az");

        using var create = await api.SendAsync(HttpMethod.Post, "/api/v1/admin/integration-clients", Access(admin), new
        {
            name = "Sandbox POS",
            ownerType = "Partner",
            ownerId = Guid.CreateVersion7(),
            businessIds = new[] { liveShop.Id },
            scopes = new[] { PartnerScopes.CodesRead },
            isSandbox = true,
            rateLimitPerMinute = 120,
        });

        Assert.Equal("sandbox_mismatch", (await api.JsonAsync(create)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sandbox_business_is_hidden_from_public_search()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        var sandboxShop = await GiftTestData.ShopAsync(api, "psb-hide");
        var liveShop = await GiftTestData.ShopAsync(api, "psb-show");
        await SetSandboxAsync(api, sandboxShop);

        using var response = await api.GetAsync("/api/v1/businesses?pageSize=50", null);
        var ids = (await api.JsonAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(liveShop.Id, ids);
        Assert.DoesNotContain(sandboxShop.Id, ids);
    }

    [Fact]
    public async Task Sandbox_redemption_and_its_reversal_write_no_ledger_entries()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        var giftId = StronglyTypedId.New<GiftId>();
        var redemption = StronglyTypedId.New<GiftRedemptionId>();
        var at = new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
        var redeemed = new GiftRedeemed(
            giftId, redemption, BusinessId.From(business), GiftKind.Voucher, GiftScope.Business, 20_00, 0, null, null, null, null, false, at, Sandbox: true);
        var reversed = new GiftRedemptionReversed(
            giftId, redemption, StronglyTypedId.New<GiftRedemptionId>(), BusinessId.From(business), 20_00, null, true, at.AddMinutes(1), Sandbox: true);

        Assert.True(await ProcessAsync(api, Message(redeemed)));
        Assert.True(await ProcessAsync(api, Message(reversed)));

        Assert.Equal(0, await BalanceAsync(api, LedgerAccount.Business(business)));
    }

    private static async Task SetSandboxAsync(ApiTestHost api, GiftShop shop)
    {
        using var response = await api.SendAsync(HttpMethod.Put, $"/api/v1/admin/businesses/{shop.Id}/sandbox", Access(shop.Shop.Admin), new { isSandbox = true });
        var body = await api.JsonAsync(response);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        Assert.True(body.GetProperty("isSandbox").GetBoolean());
    }
}
