using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>İctimai coğrafi axtarış və biznes səhifəsi (E04-T05).</summary>
[Collection(MongoCollection.Name)]
public sealed class BusinessSearchTests(MongoFixture mongo)
{
    private const string Center = "lat=40.4093&lng=49.8671";

    [Fact]
    public async Task Search_returns_only_active_businesses_nearest_first_with_filters_and_paging()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await SeedAsync(host);

        var near = await SearchAsync(host, $"{Center}&radiusKm=5");
        Assert.Equal(2, near.GetProperty("total").GetInt64());
        Assert.Equal(["Mərkəz Kafe", "Bulvar Restoran"], Names(near));
        var distances = near.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("distanceMeters").GetDouble()).ToArray();
        Assert.True(distances[0] < 10 && distances[1] is > 1000 and < 5000, string.Join(",", distances));

        Assert.Equal(["Bulvar Restoran"], Names(await SearchAsync(host, $"{Center}&type=Restaurant")));
        Assert.Equal(["Bulvar Restoran"], Names(await SearchAsync(host, $"{Center}&acceptsPool=true")));
        Assert.Equal(["Mərkəz Kafe"], Names(await SearchAsync(host, $"{Center}&acceptsNetworkVouchers=true")));
        Assert.Equal(2, (await SearchAsync(host, $"{Center}&radiusKm=50")).GetProperty("total").GetInt64());

        var all = await SearchAsync(host, "");
        Assert.Equal(3, all.GetProperty("total").GetInt64());
        Assert.Equal(["Bulvar Restoran", "Gəncə Kafe", "Mərkəz Kafe"], Names(all));
        Assert.Equal(JsonValueKind.Null, all.GetProperty("items")[0].GetProperty("distanceMeters").ValueKind);

        var second = await SearchAsync(host, "page=2&pageSize=1");
        Assert.Equal(["Gəncə Kafe"], Names(second));
        Assert.Equal(3, second.GetProperty("total").GetInt64());
        Assert.Equal(3, (await SearchAsync(host, "pageSize=500")).GetProperty("items").GetArrayLength());

        var json = all.GetRawText();
        Assert.DoesNotContain("iban", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voen", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerUserId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_search_parameters_are_rejected()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        foreach (var query in new[] { "lat=40.4", "lat=100&lng=49", $"{Center}&radiusKm=80", $"{Center}&radiusKm=0" })
        {
            using var response = await host.GetAsync($"/api/v1/businesses?{query}", null);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Public_page_shows_active_business_with_active_catalog_by_slug_or_id()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var (owner, id, pendingId) = await SeedAsync(host);
        foreach (var (title, active) in new[] { ("Çay", true), ("Gizli", false) })
        {
            using var create = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/catalog", Access(owner), new { title, priceQepik = 300, isActive = active, isGiftable = true });
            create.EnsureSuccessStatusCode();
        }

        foreach (var key in new[] { "merkez-kafe", id })
        {
            using var page = await host.GetAsync($"/api/v1/businesses/{key}", null);
            var body = await host.JsonAsync(page);
            Assert.Equal("Mərkəz Kafe", body.GetProperty("business").GetProperty("name").GetString());
            Assert.Equal(["Çay"], body.GetProperty("catalog").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        }

        using var pending = await host.GetAsync($"/api/v1/businesses/{pendingId}", null);
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
    }

    [Fact]
    public async Task Location_has_a_partial_2dsphere_index()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await host.LoginAsync("warmup@example.az");

        var indexes = await (await host.Database.GetCollection<BsonDocument>("businesses").Indexes.ListAsync(TestContext.Current.CancellationToken))
            .ToListAsync(TestContext.Current.CancellationToken);
        var geo = indexes.Single(i => i["name"] == "geo_businesses_location");
        Assert.Equal("2dsphere", geo["key"]["location"].AsString);
        Assert.Equal(new BsonDocument("isDeleted", false), geo["partialFilterExpression"]);
    }

    /// <summary>3 aktiv (mərkəz, 2 km uzaq, Gəncə) və 1 gözləyən biznes.</summary>
    private static async Task<(JsonElement Owner, string Id, string PendingId)> SeedAsync(ApiTestHost host)
    {
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, center) = await ActiveBusinessAsync(host, admin, "a@example.az", ApplicationBody("Mərkəz Kafe", "1000000001"));
        var (poolOwner, boulevard) = await ActiveBusinessAsync(host, admin, "b@example.az", ApplicationBody("Bulvar Restoran", "1000000002", 40.3953, 49.8822, "Restaurant"));
        await ActiveBusinessAsync(host, admin, "c@example.az", ApplicationBody("Gəncə Kafe", "1000000003", 40.6828, 46.3606));
        var pendingOwner = await host.LoginAsync("d@example.az");
        var pending = await ApplyAsync(host, pendingOwner, ApplicationBody("Gözləyən Kafe", "1000000004"));

        using (var pool = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{boulevard}/settings", Access(poolOwner), new { acceptsPool = true }))
        {
            pool.EnsureSuccessStatusCode();
        }

        using (var network = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{center}/settings", Access(owner), new { acceptsNetworkVouchers = true }))
        {
            network.EnsureSuccessStatusCode();
        }

        return (owner, center, pending.GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> SearchAsync(ApiTestHost host, string query)
    {
        using var response = await host.GetAsync($"/api/v1/businesses?{query}", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await host.JsonAsync(response);
    }

    private static string?[] Names(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())];
}
