using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Integration;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Issuer hədiyyələri və partnyor balansı (E13-T07): bank köçürməsi və kartla artım, balansdan buraxılış (link bir dəfə), çatmayan balans,
/// ləğvdə balansa qaytarma, yalnız öz hədiyyəsi.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerIssuerTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Issuer_funds_gifts_from_its_prepaid_balance_and_gets_the_money_back_on_cancel()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "piss");
        var (admin, client, secret) = await PartnerTestData.ClientAsync(api, "piss", [PartnerScopes.GiftsIssue, PartnerScopes.GiftsRead]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using (var topUp = await AdminTopUpAsync(api, admin, client.GetProperty("id").GetString()!, 100_00))
        {
            Assert.True(topUp.IsSuccessStatusCode, await topUp.Content.ReadAsStringAsync(Ct));
        }

        Assert.Equal(100_00, await BalanceAsync(api, partner, access));

        var issued = await IssueAsync(api, partner, access, shop, 30_00, "pi-1");
        Assert.Equal(HttpStatusCode.Created, issued.Status);
        Assert.Equal("Scheduled", issued.Body.GetProperty("status").GetString());
        Assert.Equal("ORDER-30", issued.Body.GetProperty("externalReference").GetString());
        Assert.Contains("/g/", issued.Body.GetProperty("link").GetString(), StringComparison.Ordinal);
        Assert.Equal(70_00, await BalanceAsync(api, partner, access));

        var tooMuch = await IssueAsync(api, partner, access, shop, 80_00, "pi-2");
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.Status);
        Assert.Equal("insufficient_partner_balance", tooMuch.Body.GetProperty("code").GetString());
        Assert.Equal(70_00, await BalanceAsync(api, partner, access));

        var giftId = issued.Body.GetProperty("id").GetString()!;
        using (var read = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/gifts/{giftId}", access))
        {
            var body = await api.JsonAsync(read);
            Assert.Equal((30_00L, false), (body.GetProperty("remainingQepik").GetInt64(), body.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String));
        }

        using (var cancel = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/gifts/{giftId}/cancel", access, null, "pi-c1"))
        {
            Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync(Ct));
            Assert.Equal("Cancelled", (await api.JsonAsync(cancel)).GetProperty("status").GetString());
        }

        await DeliverAsync(api, Ledger());
        Assert.Equal(100_00, await BalanceAsync(api, partner, access));

        var doc = await GiftTestData.GiftDocAsync(api, giftId);
        Assert.Equal(Guid.Parse(client.GetProperty("id").GetString()!), doc["partnerClientId"].AsGuid);
    }

    [Fact]
    public async Task Card_top_up_credits_the_balance_and_other_clients_cannot_see_the_gift()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "pcard");
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pcard", [PartnerScopes.GiftsIssue, PartnerScopes.GiftsRead]);
        var (_, other, otherSecret) = await PartnerTestData.ClientAsync(api, "pcard2", [PartnerScopes.GiftsRead]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);
        var otherAccess = await partner.AccessTokenAsync(other.GetProperty("clientId").GetString()!, otherSecret);

        string paymentUrl;
        using (var start = await partner.SendAsync(HttpMethod.Post, "/partner/v1/balance/top-ups", access, new { amountQepik = 50_00 }, "pcard-t1"))
        {
            var body = await api.JsonAsync(start);
            Assert.True(start.IsSuccessStatusCode, body.GetRawText());
            paymentUrl = body.GetProperty("paymentUrl").GetString()!;
        }

        await PayAsync(api, paymentUrl);
        await DeliverAsync(api, Fulfillment(api));
        Assert.Equal(50_00, await BalanceAsync(api, partner, access));

        var issued = await IssueAsync(api, partner, access, shop, 20_00, "pcard-g1");
        using var foreign = await partner.SendAsync(HttpMethod.Get, $"/partner/v1/gifts/{issued.Body.GetProperty("id").GetString()}", otherAccess);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private static async Task<long> BalanceAsync(ApiTestHost api, PartnerTestHost partner, string access)
    {
        using var response = await partner.SendAsync(HttpMethod.Get, "/partner/v1/balance", access);
        return (await api.JsonAsync(response)).GetProperty("balanceQepik").GetInt64();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> IssueAsync(
        ApiTestHost api, PartnerTestHost partner, string access, GiftShop shop, long amount, string key)
    {
        using var response = await partner.SendAsync(HttpMethod.Post, "/partner/v1/gifts", access, new
        {
            what = new { kind = "Voucher", scope = "Business", businessId = shop.Id, amountQepik = amount },
            recipientName = "Müştəri",
            recipientEmail = (string?)null,
            theme = "Thanks",
            message = "Kartınızla ödədiyiniz üçün təşəkkür",
            externalReference = $"ORDER-{amount / 100}",
        }, key);
        return (response.StatusCode, await api.JsonAsync(response));
    }

    private static async Task<HttpResponseMessage> AdminTopUpAsync(ApiTestHost api, JsonElement admin, string clientId, long amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/admin/integration-clients/{clientId}/top-ups", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { amountQepik = amount, bankReference = "BANK-REF-1" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Access(admin));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await api.Client.SendAsync(request, Ct);
    }
}
