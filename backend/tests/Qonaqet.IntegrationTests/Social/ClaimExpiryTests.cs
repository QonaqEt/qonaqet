using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Features.Social.Jobs;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Rezervin müddəti və buraxılması (E06-T06).</summary>
[Collection(MongoCollection.Name)]
public sealed class ClaimExpiryTests(MongoFixture mongo)
{
    [Fact]
    public async Task Unredeemed_reservations_expire_after_thirty_minutes_and_their_units_return()
    {
        mongo.SkipIfUnavailable();
        await using var host = NewHost();
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 3));
        var suspended = Id(await SuspendedAsync(host, shop, everyone, quantity: 2));
        var a = await ClaimOkAsync(host, await host.LoginAsync("a@example.az"), daily);
        var b = await ClaimOkAsync(host, await host.LoginAsync("b@example.az"), suspended);
        Assert.Equal(1, (await OfferDocAsync(host, daily))["usedToday"].AsInt32);
        Assert.Equal(1, (await OfferDocAsync(host, suspended))["remaining"].AsInt32);

        var start = host.Clock.UtcNow;
        host.Clock.UtcNow = start.AddMinutes(29);
        Assert.Equal(0, await RunAsync(host));

        host.Clock.UtcNow = start.AddMinutes(31);
        Assert.Equal(2, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));

        Assert.Equal("Expired", (await ClaimDocAsync(host, Id(a)))["status"].AsString);
        Assert.Equal("Expired", (await ClaimDocAsync(host, Id(b)))["status"].AsString);
        Assert.Equal(0, (await OfferDocAsync(host, daily))["usedToday"].AsInt32);
        Assert.Equal(2, (await OfferDocAsync(host, suspended))["remaining"].AsInt32);
    }

    [Fact]
    public async Task Parallel_job_runs_release_each_reservation_once()
    {
        mongo.SkipIfUnavailable();
        await using var host = NewHost();
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var suspended = Id(await SuspendedAsync(host, shop, everyone, quantity: 10));
        for (var i = 0; i < 6; i++)
        {
            await ClaimOkAsync(host, await host.LoginAsync($"g{i}@example.az"), suspended);
        }

        Assert.Equal(4, (await OfferDocAsync(host, suspended))["remaining"].AsInt32);
        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(31);

        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync(host)));

        Assert.Equal(6, counts.Sum());
        Assert.Equal(10, (await OfferDocAsync(host, suspended))["remaining"].AsInt32);
    }

    [Fact]
    public async Task Recipient_cancels_a_reservation_and_the_unit_returns_immediately()
    {
        mongo.SkipIfUnavailable();
        await using var host = NewHost();
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 1));
        var guest = await host.LoginAsync("guest@example.az");
        var claim = await ClaimOkAsync(host, guest, daily);

        var other = await host.LoginAsync("other@example.az");
        using (var foreign = await host.SendAsync(HttpMethod.Post, $"/api/v1/claims/{Id(claim)}/cancel", Access(other)))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        using (var sold = await ClaimAsync(host, other, daily))
        {
            Assert.Equal("quota_exhausted", await CodeAsync(host, sold));
        }

        using (var cancel = await host.SendAsync(HttpMethod.Post, $"/api/v1/claims/{Id(claim)}/cancel", Access(guest)))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        var stored = await ClaimDocAsync(host, Id(claim));
        Assert.Equal("Cancelled", stored["status"].AsString);
        Assert.Equal("recipient_cancelled", stored["cancelReason"].AsString);

        using (var again = await host.SendAsync(HttpMethod.Post, $"/api/v1/claims/{Id(claim)}/cancel", Access(guest)))
        {
            Assert.Equal("claim_cancelled", await CodeAsync(host, again));
        }

        // Vahid geri qayıtdı: başqası indi ala bilir. Ləğv olunan rezerv sahibinin limitini yemir.
        await ClaimOkAsync(host, other, daily);
    }

    private ApiTestHost NewHost() => new(mongo, configure: s => s.AddScoped<ExpireClaimsJob>());

    private static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExpireClaimsJob>().RunAsync(Ct);
    }

    private static async Task<BsonDocument> ClaimDocAsync(ApiTestHost host, string id) =>
        await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(id))).SingleAsync(Ct);
}
