using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Biznes sahibinin öz inteqrasiya client-ləri (E13-T11): yalnız öz bizneslərinə, yalnız biznes scope-ları ilə; başqasının client-i görünmür;
/// yaratma, rotasiya və ləğv audit log-a düşür; ləğv olunmuş client token ala bilmir.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class OwnerIntegrationClientTests(PaymentsFixture fixture)
{
    private const string Path = "/api/v1/business/integrations/clients";

    [Fact]
    public async Task Owner_creates_rotates_and_revokes_clients_for_own_businesses_only()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "oic");
        var other = await GiftTestData.ShopAsync(api, "oic-other");
        var owner = Access(shop.Owner);

        // Başqasının biznesi, işçi və icazəsiz scope rədd olunur.
        Assert.Equal("business_access_denied", await ErrorAsync(api, owner, new { name = "POS", businessIds = new[] { other.Id }, scopes = new[] { "codes:read" } }));
        Assert.Equal("business_access_denied", await ErrorAsync(api, Access(shop.Shop.Staff), new { name = "POS", businessIds = new[] { shop.Id }, scopes = new[] { "codes:read" } }));
        using (var scopeDenied = await api.SendAsync(
                   HttpMethod.Post, Path, owner, new { name = "POS", businessIds = new[] { shop.Id }, scopes = new[] { PartnerScopes.GiftsIssue } }))
        {
            var problem = await api.JsonAsync(scopeDenied);
            Assert.Contains(problem.GetProperty("errors").EnumerateArray(), e => e.GetProperty("code").GetString() == "scope_not_allowed");
        }

        JsonElement created;
        using (var create = await api.SendAsync(HttpMethod.Post, Path, owner, new
               {
                   name = "Kassa POS",
                   businessIds = new[] { shop.Id },
                   scopes = new[] { PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite },
               }))
        {
            created = await api.JsonAsync(create);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        var client = created.GetProperty("client");
        var id = client.GetProperty("id").GetString()!;
        var clientId = client.GetProperty("clientId").GetString()!;
        Assert.StartsWith("qc_live_", clientId, StringComparison.Ordinal);
        Assert.Equal("Business", client.GetProperty("ownerType").GetString());
        await partner.AccessTokenAsync(clientId, created.GetProperty("clientSecret").GetString()!);

        Assert.Equal([id], await ListAsync(api, owner));
        Assert.Empty(await ListAsync(api, Access(other.Owner)));
        using (var foreign = await api.SendAsync(HttpMethod.Post, $"{Path}/{id}/rotate-secret", Access(other.Owner)))
        {
            Assert.Equal("integration_client_not_found", (await api.JsonAsync(foreign)).GetProperty("code").GetString());
        }

        string rotatedSecret;
        using (var rotate = await api.SendAsync(HttpMethod.Post, $"{Path}/{id}/rotate-secret", owner))
        {
            var body = await api.JsonAsync(rotate);
            Assert.True(rotate.IsSuccessStatusCode, body.GetRawText());
            rotatedSecret = body.GetProperty("clientSecret").GetString()!;
        }

        await partner.AccessTokenAsync(clientId, rotatedSecret);

        using (var revoke = await api.SendAsync(HttpMethod.Delete, $"{Path}/{id}", owner))
        {
            Assert.True(revoke.IsSuccessStatusCode);
        }

        Assert.Empty(await ListAsync(api, owner));
        using (var token = await partner.TokenAsync(clientId, rotatedSecret))
        {
            Assert.False(token.IsSuccessStatusCode);
        }

        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var ownerId = UserId(shop.Owner);
        var actions = await context.Set<AuditLogRecord>().AsNoTracking()
            .Where(a => a.EntityType == nameof(IntegrationClient) && a.ActorId == ownerId).Select(a => a.Action).ToListAsync(Ct);
        Assert.True(actions.Count >= 3, string.Join(", ", actions));
    }

    private static async Task<string?> ErrorAsync(ApiTestHost api, string access, object body)
    {
        using var response = await api.SendAsync(HttpMethod.Post, Path, access, body);
        Assert.False(response.IsSuccessStatusCode);
        return (await api.JsonAsync(response)).GetProperty("code").GetString();
    }

    private static async Task<List<string>> ListAsync(ApiTestHost api, string access)
    {
        using var response = await api.SendAsync(HttpMethod.Get, Path, access);
        var body = await api.JsonAsync(response);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        return [.. body.EnumerateArray().Select(c => c.GetProperty("id").GetString()!)];
    }
}
