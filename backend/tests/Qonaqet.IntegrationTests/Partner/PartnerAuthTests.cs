using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>Client credentials və scope-lar (E13-T02): secret bir dəfə və hash-lə, token, rotasiya, ləğv, audience ayrılığı, client limiti.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerAuthTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Admin_created_client_gets_a_one_hour_token_for_its_scopes()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (admin, client, secret) = await PartnerTestData.ClientAsync(api, "pauth", [PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite]);
        var clientId = client.GetProperty("clientId").GetString()!;

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Set<IntegrationClient>().AsNoTracking()
                .SingleAsync(c => c.ClientId == clientId, Ct);
            Assert.DoesNotContain(secret, stored.SecretHash, StringComparison.Ordinal);
        }

        using (var list = await api.GetAsync("/api/v1/admin/integration-clients", Access(admin)))
        {
            Assert.DoesNotContain(secret, await list.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        using (var token = await partner.TokenAsync(clientId, secret, "codes:read"))
        {
            var body = await api.JsonAsync(token);
            Assert.Equal(HttpStatusCode.OK, token.StatusCode);
            Assert.Equal(
                ("Bearer", 3600, "codes:read"),
                (body.GetProperty("token_type").GetString(), body.GetProperty("expires_in").GetInt32(), body.GetProperty("scope").GetString()));

            using var me = await partner.SendAsync(HttpMethod.Get, "/partner/v1/me", body.GetProperty("access_token").GetString());
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(clientId, (await api.JsonAsync(me)).GetProperty("clientId").GetString());
        }

        using (var wrongSecret = await partner.TokenAsync(clientId, secret + "x"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
            Assert.Equal("invalid_client", (await api.JsonAsync(wrongSecret)).GetProperty("code").GetString());
        }

        using var extraScope = await partner.TokenAsync(clientId, secret, "codes:read gifts:issue");
        Assert.Equal("invalid_scope", (await api.JsonAsync(extraScope)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task App_and_partner_tokens_are_not_interchangeable()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "paud", [PartnerScopes.CodesRead]);
        var partnerToken = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);
        var user = await api.LoginAsync("paud-user@example.az");

        using (var userOnPartner = await partner.SendAsync(HttpMethod.Get, "/partner/v1/me", Access(user)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, userOnPartner.StatusCode);
        }

        using var partnerOnApp = await api.GetAsync("/api/v1/me", partnerToken);
        Assert.Equal(HttpStatusCode.Unauthorized, partnerOnApp.StatusCode);
    }

    [Fact]
    public async Task Rotation_keeps_the_old_secret_for_24_hours_and_revocation_cuts_tokens_at_once()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (admin, client, oldSecret) = await PartnerTestData.ClientAsync(api, "prot", [PartnerScopes.CodesRead]);
        var id = client.GetProperty("id").GetString();
        var clientId = client.GetProperty("clientId").GetString()!;

        string newSecret;
        using (var rotate = await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/integration-clients/{id}/rotate-secret", Access(admin)))
        {
            newSecret = (await api.JsonAsync(rotate)).GetProperty("clientSecret").GetString()!;
        }

        Assert.NotEqual(oldSecret, newSecret);
        var token = await partner.AccessTokenAsync(clientId, newSecret);
        await partner.AccessTokenAsync(clientId, oldSecret);

        api.Clock.UtcNow = api.Clock.UtcNow.AddHours(25);
        using (var expired = await partner.TokenAsync(clientId, oldSecret))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }

        using (var revoke = await api.SendAsync(HttpMethod.Delete, $"/api/v1/admin/integration-clients/{id}", Access(admin)))
        {
            Assert.True(revoke.IsSuccessStatusCode, await revoke.Content.ReadAsStringAsync(Ct));
        }

        using var afterRevoke = await partner.SendAsync(HttpMethod.Get, "/partner/v1/me", token);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Each_client_has_its_own_rate_limit()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "prl", [PartnerScopes.CodesRead], rateLimitPerMinute: 10);
        var token = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            using var me = await partner.SendAsync(HttpMethod.Get, "/partner/v1/me", token);
            statuses.Add(me.StatusCode);
        }

        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}

internal static class PartnerTestData
{
    /// <summary>Admin Api host-unda client yaradır; (admin girişi, client DTO, bir dəfə göstərilən secret).</summary>
    public static async Task<(JsonElement Admin, JsonElement Client, string Secret)> ClientAsync(
        ApiTestHost api, string prefix, string[] scopes, Guid[]? businessIds = null, bool sandbox = false, int rateLimitPerMinute = 120)
    {
        var admin = await RolesTests.LoginAsAdminAsync(api, $"{prefix}-partner-admin@example.az");
        using var create = await api.SendAsync(HttpMethod.Post, "/api/v1/admin/integration-clients", Access(admin), new
        {
            name = $"{prefix} POS",
            ownerType = "Partner",
            ownerId = Guid.CreateVersion7(),
            businessIds = businessIds ?? [],
            scopes,
            isSandbox = sandbox,
            rateLimitPerMinute,
        });
        var body = await api.JsonAsync(create);
        Assert.True(create.StatusCode == HttpStatusCode.Created, body.GetRawText());
        return (admin, body.GetProperty("client"), body.GetProperty("clientSecret").GetString()!);
    }
}
