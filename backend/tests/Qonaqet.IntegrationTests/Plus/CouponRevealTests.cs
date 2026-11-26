using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Plus;

/// <summary>
/// Kupon siyahısı və açılması (E12-T02): yalnız uyğun kuponlar görünür, unikal kod atomik verilir, paralel sorğularda eyni kod iki dəfə
/// verilmir və <c>perUserLimit</c> aşılmır. Baza digər testlərlə paylaşılır — siyahı yoxlamaları yalnız bu testin kuponlarına baxır.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class CouponRevealTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task User_sees_only_coupons_open_to_them_while_valid_and_with_an_active_brand()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-list");
        var student = await CategoryIdAsync(host, "student");
        var everyone = await PlusTestData.CouponAsync(host, setup, "Shared", "HAMIYA-10");
        var studentsOnly = await PlusTestData.CouponAsync(host, setup, categories: [student]);
        var expired = await PlusTestData.CouponAsync(
            host, setup, validFrom: host.Clock.UtcNow.AddDays(-10), validUntil: host.Clock.UtcNow.AddDays(-1));
        var user = await host.LoginAsync("plus-list-user@example.az");

        var visible = await VisibleAsync(host, user, [everyone, studentsOnly, expired]);
        var coupon = Assert.Single(visible);
        Assert.Equal(everyone, coupon.GetProperty("id").GetString());
        Assert.True(coupon.GetProperty("canReveal").GetBoolean());
        Assert.False(coupon.TryGetProperty("sharedCode", out _)); // ortaq kod yalnız açılışla verilir

        using (var notEligible = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{studentsOnly}/reveal", Access(user)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, notEligible.StatusCode);
        }

        using (var shared = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{everyone}/reveal", Access(user)))
        {
            Assert.Equal("HAMIYA-10", (await host.JsonAsync(shared)).GetProperty("code").GetString());
        }

        // Brend deaktiv olunanda kuponları gizlənir.
        using (var deactivate = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/brands/{setup.BrandId}", Access(setup.Admin),
            new { name = "Bravo", contactEmail = "plus@plus-list.az", isActive = false }))
        {
            Assert.True(deactivate.IsSuccessStatusCode, await deactivate.Content.ReadAsStringAsync(Ct));
        }

        Assert.Empty(await VisibleAsync(host, user, [everyone]));
        using var hidden = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{everyone}/reveal", Access(user));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task Unique_code_is_assigned_once_and_the_per_user_limit_holds()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-reveal");
        var couponId = await PlusTestData.CouponAsync(host, setup, perUserLimit: 1);
        await PlusTestData.AddCodesAsync(host, setup, couponId, "REVEAL-1", "REVEAL-2");
        var user = await host.LoginAsync("plus-reveal-user@example.az");

        string code;
        using (var first = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{couponId}/reveal", Access(user)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            code = (await host.JsonAsync(first)).GetProperty("code").GetString()!;
        }

        using (var second = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{couponId}/reveal", Access(user)))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("limit_reached", (await host.JsonAsync(second)).GetProperty("code").GetString());
        }

        var mine = Assert.Single(await VisibleAsync(host, user, [couponId]));
        Assert.Equal([code], mine.GetProperty("myCodes").EnumerateArray().Select(c => c.GetString()));
        Assert.False(mine.GetProperty("canReveal").GetBoolean());
    }

    [Fact]
    public async Task Parallel_reveals_never_hand_out_the_same_code_twice_or_exceed_the_limit()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var setup = await PlusTestData.BrandAsync(host, "plus-race");
        var couponId = await PlusTestData.CouponAsync(host, setup, perUserLimit: 2);
        await PlusTestData.AddCodesAsync(host, setup, couponId, [.. Enumerable.Range(1, 5).Select(i => $"RACE-{i}")]);

        // Bir istifadəçi eyni anda 6 dəfə: limit 2-dir.
        var greedy = await host.LoginAsync("plus-race-greedy@example.az");
        var greedyResults = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RevealAsync(host, greedy, couponId)));
        Assert.Equal(2, greedyResults.Count(r => r.Code is not null));
        Assert.All(greedyResults.Where(r => r.Code is null), r => Assert.Equal("limit_reached", r.Error));

        // Qalan 3 kod üçün 6 fərqli istifadəçi eyni anda.
        var users = new List<JsonElement>();
        for (var i = 0; i < 6; i++)
        {
            users.Add(await host.LoginAsync($"plus-race-{i}@example.az"));
        }

        var results = await Task.WhenAll(users.Select(u => RevealAsync(host, u, couponId)));
        var codes = greedyResults.Concat(results).Where(r => r.Code is not null).Select(r => r.Code!).ToList();
        Assert.Equal(5, codes.Count);
        Assert.Equal(5, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(results.Where(r => r.Code is null), r => Assert.Equal("coupon_sold_out", r.Error));

        var assigned = await host.Database.GetCollection<BsonDocument>("couponCodes")
            .Find(new BsonDocument { { "couponId", new BsonBinaryData(Guid.Parse(couponId), GuidRepresentation.Standard) }, { "assignedUserId", new BsonDocument("$ne", BsonNull.Value) } })
            .CountDocumentsAsync(Ct);
        Assert.Equal(5, assigned);
    }

    private static async Task<(string? Code, string? Error)> RevealAsync(ApiTestHost host, JsonElement user, string couponId)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/coupons/{couponId}/reveal", Access(user));
        var body = await host.JsonAsync(response);
        return response.IsSuccessStatusCode ? (body.GetProperty("code").GetString(), null) : (null, body.GetProperty("code").GetString());
    }

    private static async Task<IReadOnlyList<JsonElement>> VisibleAsync(ApiTestHost host, JsonElement user, IReadOnlyCollection<string> ids)
    {
        using var response = await host.GetAsync("/api/v1/coupons", Access(user));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await host.JsonAsync(response)).EnumerateArray().Where(c => ids.Contains(c.GetProperty("id").GetString()!))];
    }

    private static async Task<Guid> CategoryIdAsync(ApiTestHost host, string code)
    {
        var category = await host.Database.GetCollection<BsonDocument>("categories")
            .Find(new BsonDocument { { "code", code }, { "isDeleted", false } }).SingleAsync(Ct);
        return category["_id"].AsGuid;
    }
}
