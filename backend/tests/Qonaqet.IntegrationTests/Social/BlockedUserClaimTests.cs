using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Social.Commands.ClaimOffer;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Bloklanmış istifadəçi: claim qadağası və aktiv claim-lərin ləğvi (E06-T08).</summary>
[Collection(MongoCollection.Name)]
public sealed class BlockedUserClaimTests(MongoFixture mongo)
{
    [Fact]
    public void Claim_creating_commands_require_an_active_account() =>
        Assert.NotNull(typeof(ClaimOfferCommand).GetCustomAttributes(typeof(RequiresActiveAccountAttribute), false).SingleOrDefault());

    [Fact]
    public async Task Blocking_cancels_reserved_claims_once_and_stops_new_claims()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var coffee = Id(await SuspendedAsync(host, shop, everyone, quantity: 2));
        var tea = Id(await DailyAsync(host, shop, everyone, quota: 2));
        var guest = await host.LoginAsync("guest@example.az");
        var claim = await ClaimOkAsync(host, guest, coffee);
        Assert.Equal(1, (await OfferDocAsync(host, coffee))["remaining"].AsInt32);

        using (var block = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/users/{UserId(guest)}/block", Access(shop.Admin), new { reason = "spam" }))
        {
            Assert.True(block.IsSuccessStatusCode, await block.Content.ReadAsStringAsync(Ct));
        }

        // Hadisə Worker-də işlənir; burada consumer outbox-dakı real mesajla birbaşa çağırılır (iki dəfə: təkrar çatdırılma).
        var message = await OutboxMessageAsync(host, "UserBlocked");
        var consumer = new SocialEventsConsumer(host.Services.GetRequiredService<IServiceScopeFactory>());
        await consumer.HandleAsync(message, null!, Ct);
        await consumer.HandleAsync(message, null!, Ct);

        var stored = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(Id(claim)))).SingleAsync(Ct);
        Assert.Equal("Cancelled", stored["status"].AsString);
        Assert.Equal("user_blocked", stored["cancelReason"].AsString);
        Assert.Equal(2, (await OfferDocAsync(host, coffee))["remaining"].AsInt32);

        using var refused = await ClaimAsync(host, guest, tea);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("user_blocked", await CodeAsync(host, refused));
        Assert.Equal(0, (await OfferDocAsync(host, tea)).GetValue("usedToday", 0).AsInt32);
    }

    internal static async Task<IntegrationMessage> OutboxMessageAsync(ApiTestHost host, string eventName)
    {
        var doc = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression($"\\.{eventName}$"))).SortByDescending(d => d["occurredAt"]).FirstAsync(Ct);
        var id = doc["_id"].IsGuid ? doc["_id"].AsGuid : doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);
        return new IntegrationMessage(id, doc["type"].AsString, doc["payload"].AsString);
    }
}
