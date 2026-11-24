using Qonaqet.IntegrationTests.Identity;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>Donor atribusiyası (E08-T04): ən köhnə istifadə olunmamış ianə, FIFO xərcləmə, alıcıya yalnız ad.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class DonorAttributionTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Named_donor_appears_on_the_claims_funded_by_their_share_then_anonymous()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "attr", price: 3_00, category: "student");
        await PoolClaimTests.FundAsync(host, student, 5_00, donorName: "Leyla", email: "leyla-donor@example.az");
        host.Clock.UtcNow = host.Clock.UtcNow.AddSeconds(1);
        await PoolClaimTests.FundAsync(host, student, 10_00);

        // 1-ci claim: Leylanın 5 AZN-indən (3 xərclənir, 2 qalır).
        Assert.Equal("Leyla sizə ısmarladı 💚", await ClaimAndRedeemAsync(host, shop, itemId, student, "attr-1@example.az"));
        // 2-ci claim: Leylanın qalan 2 AZN-i hələ var → yenə Leyla; xərcləmə 2 Leyladan + 1 anonimdən.
        Assert.Equal("Leyla sizə ısmarladı 💚", await ClaimAndRedeemAsync(host, shop, itemId, student, "attr-2@example.az"));
        // 3-cü claim: Leylanın payı bitib → anonim.
        Assert.Equal("Bir xeyirxah insan sizə ısmarladı 💚", await ClaimAndRedeemAsync(host, shop, itemId, student, "attr-3@example.az"));

        var donations = await host.Database.GetCollection<BsonDocument>("poolDonations").Find(new BsonDocument("status", "Confirmed"))
            .Sort(new BsonDocument("confirmedAt", 1)).ToListAsync(Ct);
        Assert.Equal(0, donations[0]["remainingQepik"].ToInt64());
        Assert.Equal(10_00 - 4_00, donations[1]["remainingQepik"].ToInt64());

        // Reytinq üçün claim maliyyələşdirən donoru snapshot edir (alıcıya verilmir): ilk iki claim Leylanın, üçüncüsü anonim donorun.
        var claims = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument()).Sort(new BsonDocument("createdAt", 1)).ToListAsync(Ct);
        Assert.Equal(
            [donations[0]["donorUserId"], donations[0]["donorUserId"], donations[1]["donorUserId"]],
            claims.Select(c => c["funderUserId"]));

        using var mine = await host.GetAsync("/api/v1/me/donations/pools", Access(await host.LoginAsync("leyla-donor@example.az")));
        Assert.Equal(5_00, Assert.Single((await host.JsonAsync(mine)).EnumerateArray()).GetProperty("usedQepik").GetInt64());
    }

    private static async Task<string> ClaimAndRedeemAsync(ApiTestHost host, Shop shop, string itemId, string category, string email)
    {
        var user = await PoolClaimTests.VerifiedAsync(host, email, category);
        var claim = await PoolClaimTests.ClaimAsync(host, user, shop.BusinessId, itemId);

        // Alıcı ianəçi haqqında addan başqa heç nə görmür.
        var raw = claim.GetRawText();
        Assert.DoesNotContain("leyla-donor", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("donationId", raw, StringComparison.OrdinalIgnoreCase);

        using var redeem = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", SocialTestData.Access(shop.Staff),
            new { code = claim.GetProperty("codes").GetProperty("shortCode").GetString() });
        Assert.True(redeem.IsSuccessStatusCode, await redeem.Content.ReadAsStringAsync(Ct));
        return claim.GetProperty("donorDisplayText").GetString()!;
    }
}
