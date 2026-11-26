using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Plus;

/// <summary>
/// Brend tərəfindən kodun yoxlanması (E12-T03): unikal kod istifadə olunmuş kimi işarələnir, təkrar (paralel də) <c>already_redeemed</c>,
/// başqa brendin kodu tapılmır, hər yoxlama audit-ə düşür, cavabda müştərinin məlumatı yoxdur.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class BrandValidationTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Brand_marks_a_revealed_unique_code_used_once_and_it_is_audited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-check");
        var staff = await PlusTestData.BrandUserAsync(host, setup, "plus-check-staff@example.az");
        var couponId = await PlusTestData.CouponAsync(host, setup);
        await PlusTestData.AddCodesAsync(host, setup, couponId, "CHECK-1", "CHECK-2");

        // Heç kimə verilməmiş kod yoxlanılmır.
        using (var unrevealed = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(staff), new { code = "CHECK-1" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, unrevealed.StatusCode);
        }

        var customer = await host.LoginAsync("plus-check-customer@example.az");
        string code;
        using (var reveal = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{couponId}/reveal", Access(customer)))
        {
            code = (await host.JsonAsync(reveal)).GetProperty("code").GetString()!;
        }

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(staff), new { code = code.ToLowerInvariant() });
            return (response.StatusCode, Body: (await host.JsonAsync(response)).GetRawText());
        }));
        var ok = Assert.Single(attempts, a => a.StatusCode == HttpStatusCode.OK);
        Assert.All(attempts.Where(a => a.StatusCode != HttpStatusCode.OK), a => Assert.Contains("already_redeemed", a.Body, StringComparison.Ordinal));
        Assert.DoesNotContain("plus-check-customer", ok.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("assignedUserId", ok.Body, StringComparison.Ordinal);

        var audit = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression("CouponCodeRedeem"))).ToListAsync(Ct);
        Assert.Contains(audit, a => a["payload"].ToString()!.Contains(couponId, StringComparison.Ordinal));

        using var stats = await host.GetAsync("/api/v1/brand/coupons", Access(staff));
        var counts = Assert.Single((await host.JsonAsync(stats)).EnumerateArray()).GetProperty("codes");
        Assert.Equal((2, 1, 1), (counts.GetProperty("total").GetInt32(), counts.GetProperty("assigned").GetInt32(), counts.GetProperty("used").GetInt32()));
    }

    [Fact]
    public async Task Shared_codes_are_confirmed_and_other_brands_codes_and_users_are_rejected()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var mine = await PlusTestData.BrandAsync(host, "plus-own", "Bravo");
        var other = await PlusTestData.BrandAsync(host, "plus-other", "Araz");
        var staff = await PlusTestData.BrandUserAsync(host, mine, "plus-own-staff@example.az");
        await PlusTestData.CouponAsync(host, mine, "Shared", "OWN-SHARED");
        await PlusTestData.CouponAsync(host, other, "Shared", "OTHER-SHARED");

        for (var i = 0; i < 2; i++)
        {
            using var shared = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(staff), new { code = "OWN-SHARED" });
            Assert.Equal(HttpStatusCode.OK, shared.StatusCode); // ortaq kod "istifadə olunmur"
        }

        using (var foreign = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(staff), new { code = "OTHER-SHARED" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        using (var malformed = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(staff), new { code = "x" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        var plain = await host.LoginAsync("plus-own-plain@example.az");
        using var forbidden = await host.SendAsync(HttpMethod.Post, "/api/v1/brand/coupons/validate", Access(plain), new { code = "OWN-SHARED" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
