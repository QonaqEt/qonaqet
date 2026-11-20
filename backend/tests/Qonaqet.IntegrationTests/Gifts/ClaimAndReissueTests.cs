using System.Net;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Hesaba əlavə etmə və linkin yenidən verilməsi (E09-T04), göndərilən/alınan siyahılar.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class ClaimAndReissueTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Claim_binds_the_gift_to_the_account_and_the_link_keeps_working()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gclaim");
        var sender = await host.LoginAsync("gclaim-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(shop));

        // Login tələb olunur.
        using (var anonymous = await host.SendAsync(HttpMethod.Post, $"/api/v1/public/gifts/{token}/claim", null, new { }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var child = await host.LoginAsync("gclaim-child@example.az");
        using (var claim = await host.SendAsync(HttpMethod.Post, $"/api/v1/public/gifts/{token}/claim", Access(child), new { }))
        {
            Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
            var body = await host.JsonAsync(claim);
            Assert.Equal(giftId, body.GetProperty("id").GetString());
            Assert.True(body.GetProperty("gift").GetProperty("isClaimedToAccount").GetBoolean());
        }

        Assert.Equal(UserId(child), (await GiftTestData.GiftDocAsync(host, giftId))["recipientUserId"].AsGuid);

        // Təkrar eyni hesab: uğurlu; başqa hesab: gift_already_claimed.
        using (var again = await host.SendAsync(HttpMethod.Post, $"/api/v1/public/gifts/{token}/claim", Access(child), new { }))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        var other = await host.LoginAsync("gclaim-other@example.az");
        Assert.Equal("gift_already_claimed", await GiftTestData.ErrorCodeAsync(
            await host.SendAsync(HttpMethod.Post, $"/api/v1/public/gifts/{token}/claim", Access(other), new { })));

        // Link işləyir (tətbiqi olmayan valideyn), hədiyyə alıcının siyahısındadır.
        using (var page = await host.GetAsync($"/api/v1/public/gifts/{token}", null))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        using (var received = await host.GetAsync("/api/v1/gifts/received", Access(child)))
        {
            var list = await host.JsonAsync(received);
            var item = Assert.Single(list.EnumerateArray());
            Assert.Equal(giftId, item.GetProperty("id").GetString());
            Assert.True(item.GetProperty("gift").TryGetProperty("codes", out var codes) && codes.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        using (var details = await host.GetAsync($"/api/v1/gifts/{giftId}", Access(child)))
        {
            Assert.Equal("Recipient", (await host.JsonAsync(details)).GetProperty("role").GetString());
        }

        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync($"/api/v1/gifts/{giftId}", Access(other))));
    }

    [Fact]
    public async Task Only_the_sender_can_reissue_and_the_old_link_stops_working()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "greissue");
        var sender = await host.LoginAsync("greissue-sender@example.az");
        var (giftId, oldToken) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Items(shop));
        var oldHash = (await GiftTestData.GiftDocAsync(host, giftId))["linkTokenHash"].AsString;

        var stranger = await host.LoginAsync("greissue-stranger@example.az");
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(
            await host.SendAsync(HttpMethod.Post, $"/api/v1/gifts/{giftId}/reissue-link", Access(stranger), new { })));

        using var reissued = await host.SendAsync(HttpMethod.Post, $"/api/v1/gifts/{giftId}/reissue-link", Access(sender), new { });
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
        var newToken = new Uri((await host.JsonAsync(reissued)).GetProperty("shareUrl").GetString()!).AbsolutePath["/g/".Length..];
        Assert.NotEqual(oldToken, newToken);
        Assert.NotEqual(oldHash, (await GiftTestData.GiftDocAsync(host, giftId))["linkTokenHash"].AsString);

        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await host.GetAsync($"/api/v1/public/gifts/{oldToken}", null)));
        using (var page = await host.GetAsync($"/api/v1/public/gifts/{newToken}", null))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        using var sent = await host.GetAsync("/api/v1/gifts/sent", Access(sender));
        var mine = Assert.Single((await host.JsonAsync(sent)).EnumerateArray());
        Assert.Equal("Opened", mine.GetProperty("status").GetString());
        Assert.False(mine.GetProperty("canCancel").GetBoolean());
        Assert.True(mine.GetProperty("canReissueLink").GetBoolean());
        Assert.False(mine.TryGetProperty("codes", out _));
    }
}
