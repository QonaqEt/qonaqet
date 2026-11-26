using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Plus;

/// <summary>QONAQET Plus admin (E12-T01): brendlər, brend istifadəçisi, kuponlar və unikal kodlar.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PlusAdminTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Admin_creates_a_brand_binds_a_brand_user_and_loads_unique_codes()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-admin");
        await PlusTestData.BrandUserAsync(host, setup, "plus-admin-staff@example.az");

        var user = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument("email", "plus-admin-staff@example.az")).SingleAsync(Ct);
        Assert.Equal(Guid.Parse(setup.BrandId), user["brandId"].AsGuid);
        Assert.Contains("BrandUser", user["roles"].AsBsonArray.Select(r => r.AsString));

        var couponId = await PlusTestData.CouponAsync(host, setup);
        var import = await PlusTestData.AddCodesAsync(host, setup, couponId, "BRAVO-001", "bravo-002", "BRAVO-001");
        Assert.Equal((2, 1), (import.GetProperty("added").GetInt32(), import.GetProperty("duplicates").GetInt32()));

        // Təkrar yükləmədə artıq olan kod (unikal indeks) atlanır.
        var again = await PlusTestData.AddCodesAsync(host, setup, couponId, "BRAVO-002", "BRAVO-003");
        Assert.Equal((1, 1), (again.GetProperty("added").GetInt32(), again.GetProperty("duplicates").GetInt32()));

        using var list = await host.GetAsync($"/api/v1/admin/coupons?brandId={setup.BrandId}", Access(setup.Admin));
        var coupon = Assert.Single((await host.JsonAsync(list)).EnumerateArray());
        Assert.Equal(3, coupon.GetProperty("codes").GetProperty("total").GetInt32());
        Assert.Equal("Unique", coupon.GetProperty("codeType").GetString());
    }

    [Fact]
    public async Task Coupon_input_is_validated_and_only_admins_manage_plus()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-valid");
        var shared = await PlusTestData.CouponAsync(host, setup, "Shared", "bravo20");

        using (var codes = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/coupons/{shared}/codes", Access(setup.Admin), new { codes = new[] { "X-001" } }))
        {
            Assert.Equal(HttpStatusCode.Conflict, codes.StatusCode);
        }

        var valid = host.Clock.UtcNow;
        using (var noCode = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/coupons", Access(setup.Admin), new
        {
            brandId = setup.BrandId, codeType = "Shared", title = "Kodsuz", perUserLimit = 1, validFrom = valid, validUntil = valid.AddDays(1),
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noCode.StatusCode);
        }

        using (var badDates = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/coupons", Access(setup.Admin), new
        {
            brandId = setup.BrandId, codeType = "Unique", title = "Tərs", perUserLimit = 1, validFrom = valid, validUntil = valid.AddDays(-1),
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badDates.StatusCode);
        }

        using (var unknownCategory = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/coupons", Access(setup.Admin), new
        {
            brandId = setup.BrandId, codeType = "Unique", title = "Naməlum", eligibleCategoryIds = new[] { Guid.CreateVersion7() }, perUserLimit = 1,
            validFrom = valid, validUntil = valid.AddDays(1),
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownCategory.StatusCode);
        }

        var plain = await host.LoginAsync("plus-valid-user@example.az");
        using var forbidden = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/brands", Access(plain), new { name = "X", contactEmail = "a@b.az" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
