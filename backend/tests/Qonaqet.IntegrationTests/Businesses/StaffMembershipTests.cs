using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>İşçi üzvlükləri (E04-T02).</summary>
[Collection(MongoCollection.Name)]
public sealed class StaffMembershipTests(MongoFixture mongo)
{
    [Fact]
    public async Task Owner_adds_staff_by_email_and_staff_sees_only_operational_data()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        var staff = await host.LoginAsync("barista@example.az");

        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "Barista@Example.az" }))
        {
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
            Assert.Equal("Staff", (await host.JsonAsync(add)).GetProperty("role").GetString());
        }

        // Giriş üzvlükdən yoxlanılır: köhnə token ilə də işləyir.
        using (var view = await host.GetAsync($"/api/v1/business/{id}", Access(staff)))
        {
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
            var body = await host.JsonAsync(view);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("iban").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("voen").ValueKind);
        }

        using (var list = await host.GetAsync($"/api/v1/business/{id}/staff", Access(staff)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Equal("business_access_denied", (await host.JsonAsync(list)).GetProperty("code").GetString());
        }

        using (var list = await host.GetAsync($"/api/v1/business/{id}/staff", Access(owner)))
        {
            var members = (await host.JsonAsync(list)).EnumerateArray().Select(m => (m.GetProperty("email").GetString(), m.GetProperty("role").GetString())).ToArray();
            Assert.Equal([("owner@example.az", "Owner"), ("barista@example.az", "Staff")], members);
        }

        using var whoami = await host.GetAsync("/__test/auth/whoami", Access(await RefreshAsync(host, staff)));
        Assert.Contains("BusinessStaff", (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True(await AuditExistsAsync(host, "BusinessAddMember"));
    }

    [Fact]
    public async Task Unknown_emails_duplicates_and_outsiders_are_rejected()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        var outsider = await host.LoginAsync("outsider@example.az");

        using var unknown = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "nobody@example.az" });
        Assert.Equal("staff_account_not_found", (await host.JsonAsync(unknown)).GetProperty("code").GetString());

        using var self = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "owner@example.az" });
        Assert.Equal("staff_already_member", (await host.JsonAsync(self)).GetProperty("code").GetString());

        using var foreign = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(outsider), new { email = "outsider@example.az" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var peek = await host.GetAsync($"/api/v1/business/{id}", Access(outsider));
        Assert.Equal(HttpStatusCode.Forbidden, peek.StatusCode);

        using var missing = await host.GetAsync($"/api/v1/business/{Guid.CreateVersion7()}", Access(outsider));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }

    [Fact]
    public async Task Removing_staff_soft_deletes_the_membership_and_revokes_access()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        var staff = await host.LoginAsync("barista@example.az");
        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "barista@example.az" }))
        {
            add.EnsureSuccessStatusCode();
        }

        var staffId = staff.GetProperty("userId").GetString();
        var ownerId = owner.GetProperty("userId").GetString();
        using (var remove = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{id}/staff/{staffId}?reason=left", Access(owner)))
        {
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        }

        using (var removeOwner = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{id}/staff/{ownerId}", Access(owner)))
        {
            Assert.Equal("cannot_remove_owner", (await host.JsonAsync(removeOwner)).GetProperty("code").GetString());
        }

        using (var view = await host.GetAsync($"/api/v1/business/{id}", Access(staff)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, view.StatusCode);
        }

        var stored = await host.Database.GetCollection<BsonDocument>("businessMemberships")
            .Find(new BsonDocument("userId", new BsonBinaryData(Guid.Parse(staffId!), GuidRepresentation.Standard)))
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(stored["isDeleted"].AsBoolean);
        Assert.Equal("left", stored["deleteReason"].AsString);

        using var whoami = await host.GetAsync("/__test/auth/whoami", Access(await RefreshAsync(host, staff)));
        Assert.DoesNotContain("BusinessStaff", (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True(await AuditExistsAsync(host, "BusinessRemoveMember"));
    }
}
