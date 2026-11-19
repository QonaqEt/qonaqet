using System.Net;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>Şəbəkələr: bir sahibin bir neçə biznesi (E04-T06).</summary>
[Collection(MongoCollection.Name)]
public sealed class BusinessChainTests(MongoFixture mongo)
{
    [Fact]
    public async Task One_owner_runs_several_businesses_and_each_call_names_its_business_explicitly()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, first) = await ActiveBusinessAsync(host, admin, "chain@example.az", ApplicationBody("Şəbəkə Nizami", "3000000001"));
        var second = (await ApplyAsync(host, owner, ApplicationBody("Şəbəkə Yasamal", "3000000002"))).GetProperty("id").GetString()!;
        await ActivateAsync(host, admin, second);
        var third = (await ApplyAsync(host, owner, ApplicationBody("Şəbəkə Xətai", "3000000003"))).GetProperty("id").GetString()!;
        var (_, foreign) = await ActiveBusinessAsync(host, admin, "rival@example.az", ApplicationBody("Rəqib Kafe", "3000000009"));

        using (var mine = await host.GetAsync("/api/v1/business/mine", Access(owner)))
        {
            var body = await host.JsonAsync(mine);
            var businesses = body.GetProperty("businesses").EnumerateArray()
                .Select(b => (b.GetProperty("businessId").GetString(), b.GetProperty("role").GetString())).ToArray();
            Assert.Equal([(first, "Owner"), (second, "Owner")], businesses);
            var pending = Assert.Single(body.GetProperty("applications").EnumerateArray());
            Assert.Equal(third, pending.GetProperty("businessId").GetString());
            Assert.Equal("Pending", pending.GetProperty("status").GetString());
        }

        // Eyni token ilə hər iki biznesin kataloqu ayrı-ayrılıqda idarə olunur.
        foreach (var (business, title) in new[] { (first, "Nizami çayı"), (second, "Yasamal çayı") })
        {
            using var create = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{business}/catalog", Access(owner), new { title, priceQepik = 200, isActive = true, isGiftable = true });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        using (var catalog = await host.GetAsync($"/api/v1/business/{second}/catalog", Access(owner)))
        {
            Assert.Equal(["Yasamal çayı"], (await host.JsonAsync(catalog)).EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        }

        // Başqa sahibin biznesi və hələ aktivləşdirilməmiş öz müraciəti panel üçün bağlıdır.
        foreach (var path in new[] { $"/api/v1/business/{foreign}", $"/api/v1/business/{foreign}/catalog", $"/api/v1/business/{foreign}/staff", $"/api/v1/business/{third}/catalog" })
        {
            using var denied = await host.GetAsync(path, Access(owner));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var settings = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{foreign}/settings", Access(owner), new { acceptsPool = true });
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.Equal("business_access_denied", (await host.JsonAsync(settings)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Roles_differ_per_business_owner_in_one_is_only_staff_in_another()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (alice, aliceBusiness) = await ActiveBusinessAsync(host, admin, "alice@example.az", ApplicationBody("Alisa Kafe", "4000000001"));
        var (bob, bobBusiness) = await ActiveBusinessAsync(host, admin, "bob@example.az", ApplicationBody("Bob Kafe", "4000000002"));
        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{bobBusiness}/staff", Access(bob), new { email = "alice@example.az" }))
        {
            add.EnsureSuccessStatusCode();
        }

        using (var own = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{aliceBusiness}/settings", Access(alice), new { acceptsPool = true }))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        using (var asStaff = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{bobBusiness}/settings", Access(alice), new { acceptsPool = true }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        }

        using (var view = await host.GetAsync($"/api/v1/business/{bobBusiness}/catalog", Access(alice)))
        {
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        }

        var refreshed = await RefreshAsync(host, alice);
        using var whoami = await host.GetAsync("/__test/auth/whoami", Access(refreshed));
        var roles = (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains("BusinessOwner", roles);
        Assert.Contains("BusinessStaff", roles);
    }
}
