using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>Biznes müraciəti və admin aktivləşdirmə (E04-T01).</summary>
[Collection(MongoCollection.Name)]
public sealed class BusinessOnboardingTests(MongoFixture mongo)
{
    [Fact]
    public async Task Application_is_pending_until_admin_activates_it_and_then_the_applicant_is_owner()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var applicant = await host.LoginAsync("owner@example.az");

        var business = await ApplyAsync(host, applicant);
        Assert.Equal("Pending", business.GetProperty("status").GetString());
        Assert.Equal("cay-evi", business.GetProperty("slug").GetString());
        Assert.Equal("+994501234567", business.GetProperty("phone").GetString());
        var id = business.GetProperty("id").GetString()!;

        using (var queue = await host.GetAsync("/api/v1/admin/businesses?status=Pending", Access(admin)))
        {
            var page = await host.JsonAsync(queue);
            Assert.Equal(1, page.GetProperty("total").GetInt64());
            Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetString());
        }

        await ActivateAsync(host, admin, id);
        var owner = await RefreshAsync(host, applicant);
        using var whoami = await host.GetAsync("/__test/auth/whoami", Access(owner));
        var roles = (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains("BusinessOwner", roles);

        using var details = await host.GetAsync($"/api/v1/admin/businesses/{id}", Access(admin));
        Assert.Equal("Active", (await host.JsonAsync(details)).GetProperty("status").GetString());
        Assert.True(await AuditExistsAsync(host, "BusinessActivate"));
    }

    [Fact]
    public async Task Admin_can_suspend_and_reactivate_and_plain_users_cannot()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");

        using (var forbidden = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/businesses/{id}/activate", Access(owner)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var suspend = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/businesses/{id}/suspend", Access(admin), new { reason = "Şikayətlər" }))
        {
            Assert.Equal("Suspended", (await host.JsonAsync(suspend)).GetProperty("status").GetString());
        }

        Assert.True(await AuditExistsAsync(host, "BusinessSuspend"));
        await ActivateAsync(host, admin, id);
    }

    [Fact]
    public async Task Voen_is_unique_one_pending_application_per_user_and_fields_are_validated()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var first = await host.LoginAsync("first@example.az");
        var second = await host.LoginAsync("second@example.az");
        await ApplyAsync(host, first);

        using var pending = await host.SendAsync(HttpMethod.Post, "/api/v1/businesses/apply", Access(first), ApplicationBody(voen: "1111111111"));
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal("business_application_pending", (await host.JsonAsync(pending)).GetProperty("code").GetString());

        using var duplicate = await host.SendAsync(HttpMethod.Post, "/api/v1/businesses/apply", Access(second), ApplicationBody());
        Assert.Equal("voen_already_registered", (await host.JsonAsync(duplicate)).GetProperty("code").GetString());

        var sameName = await ApplyAsync(host, second, ApplicationBody(voen: "2222222222"));
        Assert.Equal("cay-evi-2", sameName.GetProperty("slug").GetString());

        var third = await host.LoginAsync("third@example.az");
        using var invalid = await host.SendAsync(HttpMethod.Post, "/api/v1/businesses/apply", Access(third), new
        {
            name = "X",
            type = "Cafe",
            voen = "12",
            address = "Bakı",
            latitude = 95.0,
            longitude = 49.0,
            phone = "123",
            email = "bad",
            iban = "AZ00",
            workingHours = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var body = await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var code in new[] { "voen_invalid", "location_invalid", "phone_invalid", "iban_invalid" })
        {
            Assert.Contains(code, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Voen_and_slug_have_partial_unique_indexes()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await host.LoginAsync("warmup@example.az");

        var indexes = await (await host.Database.GetCollection<BsonDocument>("businesses").Indexes.ListAsync(TestContext.Current.CancellationToken))
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (var name in new[] { "ux_businesses_voen", "ux_businesses_slug" })
        {
            var index = indexes.Single(i => i["name"] == name);
            Assert.True(index["unique"].AsBoolean);
            Assert.Equal(new BsonDocument("isDeleted", false), index["partialFilterExpression"]);
        }
    }
}
