using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Korporativ hesablar (E10-T01): admin şirkət yaradır, istifadəçini bağlayır, CorporateUser rolu.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CorporateAccountTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Admin_creates_a_corporate_and_assigns_a_corporate_user()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "cacct", "Azərsun");

        using (var me = await host.GetAsync("/api/v1/corporate/me", Access(setup.Hr)))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var body = await host.JsonAsync(me);
            Assert.Equal("Azərsun", body.GetProperty("name").GetString());
            Assert.Equal("#2A1A3E", body.GetProperty("brandColor").GetString());
        }

        var user = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument("email", "cacct-hr@example.az")).SingleAsync(Ct);
        Assert.Equal(Guid.Parse(setup.CorporateId), user["corporateId"].AsGuid);
        Assert.Contains("CorporateUser", user["roles"].AsBsonArray.Select(r => r.AsString));

        // VÖEN unikal, giriş yoxlanılır.
        using (var duplicate = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/corporates", Access(setup.Admin),
            new { name = "Başqa", voen = setup.Voen, contactEmail = "x@y.az" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }

        using (var invalid = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/corporates", Access(setup.Admin),
            new { name = "Səhv", voen = "123", brandColor = "red", contactEmail = "x@y.az" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        // Adi istifadəçi korporativ portala və admin endpoint-inə girə bilmir.
        var plain = await host.LoginAsync("cacct-plain@example.az");
        using (var portal = await host.GetAsync("/api/v1/corporate/me", Access(plain)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, portal.StatusCode);
        }

        using (var admin = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/corporates", Access(setup.Hr), new { name = "X", voen = "1111111111", contactEmail = "a@b.az" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        }

        using var unknown = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporates/{setup.CorporateId}/users", Access(setup.Admin), new { email = "nobody@example.az" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
