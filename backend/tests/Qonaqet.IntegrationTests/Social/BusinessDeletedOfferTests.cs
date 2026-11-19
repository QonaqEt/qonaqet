using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Biznes silinəndə təkliflər və aktiv claim-lər (E06-T09).</summary>
[Collection(MongoCollection.Name)]
public sealed class BusinessDeletedOfferTests(MongoFixture mongo)
{
    [Fact]
    public async Task Deleting_a_business_cancels_its_reservations_and_soft_deletes_its_offers_until_restore()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 3));
        var suspended = Id(await SuspendedAsync(host, shop, everyone, quantity: 2));
        var ownerDeleted = Id(await DailyAsync(host, shop, everyone, quota: 1, title: "Köhnə"));
        using (var own = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{shop.BusinessId}/offers/{ownerDeleted}", Access(shop.Owner)))
        {
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        }

        var a = await ClaimOkAsync(host, await host.LoginAsync("a@example.az"), daily);
        var redeemedLogin = await host.LoginAsync("b@example.az");
        var b = await ClaimOkAsync(host, redeemedLogin, suspended);
        using (await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", Access(shop.Staff),
            new { code = b.GetProperty("codes").GetProperty("qrPayload").GetString() }))
        {
        }

        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/businesses/{shop.BusinessId}?reason=closed", Access(shop.Admin)))
        {
            Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync(Ct));
        }

        var consumer = new SocialEventsConsumer(host.Services.GetRequiredService<IServiceScopeFactory>());
        var deleted = await BlockedUserClaimTests.OutboxMessageAsync(host, "BusinessDeleted");
        await consumer.HandleAsync(deleted, null!, Ct);
        await consumer.HandleAsync(deleted, null!, Ct);

        // Rezerv ləğv olundu (vahid təklifə qayıtdı), təhvil verilmiş claim toxunulmadı.
        Assert.Equal("Cancelled", (await ClaimDocAsync(host, Id(a)))["status"].AsString);
        Assert.Equal("business_deleted", (await ClaimDocAsync(host, Id(a)))["cancelReason"].AsString);
        Assert.Equal("Redeemed", (await ClaimDocAsync(host, Id(b)))["status"].AsString);

        foreach (var id in new[] { daily, suspended })
        {
            var doc = await OfferDocAsync(host, id);
            Assert.True(doc["isDeleted"].AsBoolean);
            Assert.Equal("business_deleted", doc["deleteReason"].AsString);
        }

        Assert.Equal(0, (await OfferDocAsync(host, daily))["usedToday"].AsInt32);
        Assert.Equal("offer deleted", (await OfferDocAsync(host, ownerDeleted))["deleteReason"].AsString);
        using (var publicList = await host.GetAsync($"/api/v1/offers?businessId={shop.BusinessId}", null))
        {
            Assert.Equal(0, (await host.JsonAsync(publicList)).GetArrayLength());
        }

        // Bərpa: kaskadla silinən təkliflər qayıdır, sahibin ayrıca sildiyi silinmiş qalır.
        using (var restore = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/businesses/{shop.BusinessId}/restore", Access(shop.Admin)))
        {
            Assert.True(restore.IsSuccessStatusCode, await restore.Content.ReadAsStringAsync(Ct));
        }

        await consumer.HandleAsync(await BlockedUserClaimTests.OutboxMessageAsync(host, "BusinessRestored"), null!, Ct);
        Assert.False((await OfferDocAsync(host, daily))["isDeleted"].AsBoolean);
        Assert.False((await OfferDocAsync(host, suspended))["isDeleted"].AsBoolean);
        Assert.True((await OfferDocAsync(host, ownerDeleted))["isDeleted"].AsBoolean);
        Assert.Equal("Cancelled", (await ClaimDocAsync(host, Id(a)))["status"].AsString);
    }

    private static async Task<BsonDocument> ClaimDocAsync(ApiTestHost host, string id) =>
        await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(id))).SingleAsync(Ct);
}
