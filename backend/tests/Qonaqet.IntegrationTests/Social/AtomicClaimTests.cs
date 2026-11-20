using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Social;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Atomik claim və gündəlik kvotanın lazy sıfırlanması (E06-T02).</summary>
[Collection(MongoCollection.Name)]
public sealed class AtomicClaimTests(MongoFixture mongo)
{
    [Fact]
    public async Task Hundred_parallel_takes_never_exceed_the_daily_quota_or_the_suspended_quantity()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 10));
        var suspended = Id(await SuspendedAsync(host, shop, everyone, quantity: 7));
        var today = Offer.DateKey(BakuTime.ToBakuDate(host.Clock.UtcNow));

        Assert.Equal(10, await TakeInParallelAsync(host, daily, today, 100));
        Assert.Equal(7, await TakeInParallelAsync(host, suspended, today, 100));

        var dailyDoc = await OfferDocAsync(host, daily);
        Assert.Equal(10, dailyDoc["usedToday"].AsInt32);
        Assert.Equal(today, dailyDoc["quotaDate"].AsString);
        Assert.Equal(0, (await OfferDocAsync(host, suspended))["remaining"].AsInt32);
    }

    [Fact]
    public async Task Daily_quota_resets_lazily_on_a_new_baku_day()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 2));
        var yesterday = Offer.DateKey(BakuTime.ToBakuDate(host.Clock.UtcNow).AddDays(-1));
        await host.Database.GetCollection<BsonDocument>("offers").UpdateOneAsync(
            new BsonDocument("_id", Bin(daily)), new BsonDocument("$set", new BsonDocument { { "quotaDate", yesterday }, { "usedToday", 2 } }), cancellationToken: Ct);

        // Dünən bitmişdi; bu gün yenidən tam kvota (siyahıda da, claim-də də).
        using (var list = await host.GetAsync($"/api/v1/offers?businessId={shop.BusinessId}", null))
        {
            Assert.Equal(2, (await host.JsonAsync(list)).EnumerateArray().Single().GetProperty("available").GetInt32());
        }

        var guest = await host.LoginAsync("guest@example.az");
        var claim = await ClaimOkAsync(host, guest, daily);
        Assert.Equal("Reserved", claim.GetProperty("status").GetString());
        Assert.Equal("Çay Evi sizə ısmarladı 💚", claim.GetProperty("donorDisplayText").GetString());

        var doc = await OfferDocAsync(host, daily);
        Assert.Equal(1, doc["usedToday"].AsInt32);
        Assert.Equal(Offer.DateKey(BakuTime.ToBakuDate(host.Clock.UtcNow)), doc["quotaDate"].AsString);

        var stored = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(Id(claim)))).SingleAsync(Ct);
        Assert.Equal("Reserved", stored["status"].AsString);
        Assert.Equal("BusinessDaily", stored["offerType"].AsString);
        Assert.Equal(host.Clock.UtcNow.AddMinutes(30).UtcDateTime, stored["expiresAt"].ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Parallel_claims_from_many_users_return_quota_exhausted_once_the_offer_runs_out()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = Id(await DailyAsync(host, shop, everyone, quota: 5));

        var guests = new List<System.Text.Json.JsonElement>();
        for (var i = 0; i < 20; i++)
        {
            guests.Add(await host.LoginAsync($"guest{i}@example.az"));
        }

        var responses = await Task.WhenAll(guests.Select(g => ClaimAsync(host, g, daily)));
        try
        {
            Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            foreach (var failed in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
                Assert.Equal("quota_exhausted", await CodeAsync(host, failed));
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.Equal(5, (await OfferDocAsync(host, daily))["usedToday"].AsInt32);
        Assert.Equal(5, await host.Database.GetCollection<BsonDocument>("claims").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Eligibility_window_and_availability_are_checked()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var student = await CategoryAsync(host, "student");
        var everyone = await CategoryAsync(host, "everyone");
        var studentOffer = Id(await DailyAsync(host, shop, student, quota: 5));

        var guest = await host.LoginAsync("guest@example.az");
        using (var notEligible = await ClaimAsync(host, guest, studentOffer))
        {
            Assert.Equal(HttpStatusCode.Forbidden, notEligible.StatusCode);
            Assert.Equal("category_not_eligible", await CodeAsync(host, notEligible));
        }

        // Müddəti bitmiş təsdiq hüquq vermir; qüvvədə olan verir.
        await VerifyAsync(host, "guest@example.az", student, host.Clock.UtcNow.AddMinutes(-1));
        using (var expired = await ClaimAsync(host, guest, studentOffer))
        {
            Assert.Equal("category_not_eligible", await CodeAsync(host, expired));
        }

        var studentUser = await host.LoginAsync("student@example.az");
        await VerifyAsync(host, "student@example.az", student);
        await ClaimOkAsync(host, studentUser, studentOffer);

        // Pəncərədən kənar: indidən 2–3 saat sonra açılan təklif.
        var local = BakuTime.ToBakuTime(host.Clock.UtcNow);
        var later = await DailyAsync(host, shop, everyone, extra: new
        {
            categoryId = everyone, title = "Axşam şorbası", dailyQuota = 5,
            availableFrom = local.AddHours(2).ToString("HH:mm", CultureInfo.InvariantCulture),
            availableTo = local.AddHours(3).ToString("HH:mm", CultureInfo.InvariantCulture),
        });
        using (var closed = await ClaimAsync(host, guest, Id(later)))
        {
            Assert.Equal("offer_outside_window", await CodeAsync(host, closed));
        }

        using (var missing = await ClaimAsync(host, guest, Guid.CreateVersion7().ToString()))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var anonymous = await host.SendAsync(HttpMethod.Post, $"/api/v1/offers/{studentOffer}/claim", null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    private static async Task<int> TakeInParallelAsync(ApiTestHost host, string offerId, string today, int attempts)
    {
        Offer offer;
        using (var scope = host.Services.CreateScope())
        {
            offer = (await scope.ServiceProvider.GetRequiredService<IOfferRepository>().GetAsync(OfferId.From(Guid.Parse(offerId)), Ct))!;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(async _ =>
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IOfferRepository>().TryTakeUnitAsync(offer, today, Ct);
        }));
        return results.Count(r => r);
    }
}
