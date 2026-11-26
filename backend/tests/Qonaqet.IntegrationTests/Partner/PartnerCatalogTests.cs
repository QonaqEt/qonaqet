using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Integration;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Kataloq sinxronu (E13-T06): externalId üzrə upsert, dəyişməyən yazılmır, prune yalnız istəniləndə və yalnız sinxron məhsullara,
/// <c>catalog.synced</c> hadisəsi.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerCatalogTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Pos_menu_is_upserted_by_external_id_and_pruned_only_on_request()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "pcat");
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pcat", [PartnerScopes.CatalogWrite], businessIds: [shop.Id]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);
        var path = $"/partner/v1/catalog/{shop.Id}";

        var first = await SyncAsync(api, partner, access, path, new[]
        {
            new { externalId = "POS-1", title = "Çay", priceQepik = 150L, group = "İsti içkilər" },
            new { externalId = "POS-2", title = "Paxlava", priceQepik = 300L, group = "Şirniyyat" },
        });
        Assert.Equal((2, 0, 0, 0), first);

        // POS-1 dəyişmir, POS-2 qiyməti dəyişir, POS-2 olmadan prune etməsək heç nə silinmir.
        var second = await SyncAsync(api, partner, access, path, new[]
        {
            new { externalId = "POS-1", title = "Çay", priceQepik = 150L, group = "İsti içkilər" },
            new { externalId = "POS-2", title = "Paxlava", priceQepik = 350L, group = "Şirniyyat" },
        });
        Assert.Equal((0, 1, 1, 0), second);

        var withoutPrune = await SyncAsync(api, partner, access, path, new[] { new { externalId = "POS-1", title = "Çay", priceQepik = 150L, group = "İsti içkilər" } });
        Assert.Equal(0, withoutPrune.Pruned);

        var pruned = await SyncAsync(
            api, partner, access, path + "?prune=true", new[] { new { externalId = "POS-1", title = "Çay", priceQepik = 150L, group = "İsti içkilər" } });
        Assert.Equal(1, pruned.Pruned);

        using (var catalog = await partner.SendAsync(HttpMethod.Get, path, access))
        {
            var items = (await api.JsonAsync(catalog)).EnumerateArray().ToList();
            Assert.DoesNotContain(items, i => i.GetProperty("externalId").GetString() == "POS-2");
            Assert.Contains(items, i => i.GetProperty("externalId").GetString() == "POS-1");
            Assert.Contains(items, i => i.GetProperty("externalId").ValueKind == System.Text.Json.JsonValueKind.Null); // əl ilə yaradılanlar qalır
        }

        var deleted = await api.Database.GetCollection<BsonDocument>("catalogItems")
            .Find(new BsonDocument { { "externalId", "POS-2" }, { "isDeleted", true } }).SingleAsync(Ct);
        Assert.Equal("catalog_sync_prune", deleted["deleteReason"].AsString);
        Assert.Equal(4, await api.Database.GetCollection<BsonDocument>("outboxMessages")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression("CatalogSynced")), cancellationToken: Ct));
    }

    [Fact]
    public async Task Duplicate_external_ids_and_foreign_businesses_are_rejected()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "pcat2");
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pcat2", [PartnerScopes.CatalogWrite], businessIds: [shop.Id]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using (var duplicate = await partner.SendAsync(HttpMethod.Put, $"/partner/v1/catalog/{shop.Id}", access, new
        {
            items = new[] { new { externalId = "X", title = "A", priceQepik = 100L }, new { externalId = "X", title = "B", priceQepik = 100L } },
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }

        using var foreign = await partner.SendAsync(HttpMethod.Put, $"/partner/v1/catalog/{Guid.CreateVersion7()}", access, new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    private static async Task<(int Created, int Updated, int Unchanged, int Pruned)> SyncAsync(
        Identity.ApiTestHost api, PartnerTestHost partner, string access, string path, object items)
    {
        using var response = await partner.SendAsync(HttpMethod.Put, path, access, new { items });
        var body = await api.JsonAsync(response);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        return (body.GetProperty("created").GetInt32(), body.GetProperty("updated").GetInt32(), body.GetProperty("unchanged").GetInt32(),
            body.GetProperty("pruned").GetInt32());
    }
}
