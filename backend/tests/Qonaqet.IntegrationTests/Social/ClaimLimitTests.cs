using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Limitlər və ləyaqətlilik qaydaları (E06-T03).</summary>
[Collection(MongoCollection.Name)]
public sealed class ClaimLimitTests(MongoFixture mongo)
{
    [Fact]
    public async Task One_active_reservation_at_a_time_and_one_claim_per_offer_per_day()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var student = await CategoryAsync(host, "student");
        var tea = Id(await DailyAsync(host, shop, student, quota: 5));
        var soup = Id(await DailyAsync(host, shop, student, quota: 5, title: "Şorba"));
        var user = await host.LoginAsync("student@example.az");
        await VerifyAsync(host, "student@example.az", student);

        var first = await ClaimOkAsync(host, user, tea);

        // Aktiv rezerv varkən ikinci claim olmur (başqa təklifdən də).
        using (var second = await ClaimAsync(host, user, soup))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("limit_reached", await CodeAsync(host, second));
        }

        // Rezerv təhvil verildi → bu təklifdən bu gün yenə olmur, başqasından olur (tələbə: gündə 2).
        await SetStatusAsync(host, Id(first), "Redeemed");
        using (var again = await ClaimAsync(host, user, tea))
        {
            Assert.Equal("limit_reached", await CodeAsync(host, again));
        }

        var other = await ClaimOkAsync(host, user, soup);
        await SetStatusAsync(host, Id(other), "Redeemed");

        // Gündəlik limit (tələbə üçün 2) doldu.
        var cake = Id(await DailyAsync(host, shop, student, quota: 5, title: "Şirniyyat"));
        using (var third = await ClaimAsync(host, user, cake))
        {
            Assert.Equal("limit_reached", await CodeAsync(host, third));
        }

        // Kvota yalnız uğurlu claim-lərə xərcləndi.
        Assert.Equal(0, (await OfferDocAsync(host, cake)).GetValue("usedToday", 0).AsInt32);
    }

    [Fact]
    public async Task The_most_generous_limit_among_the_users_categories_applies()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var veteran = await CategoryAsync(host, "veteran-family");
        var offers = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            offers.Add(Id(await DailyAsync(host, shop, everyone, quota: 5, title: $"Təklif {i}")));
        }

        // "everyone": gündə 1. Eyni istifadəçi qazi ailəsi kimi də təsdiqlənibsə gündə 3 (ən səxavətli).
        var guest = await host.LoginAsync("guest@example.az");
        await SetStatusAsync(host, Id(await ClaimOkAsync(host, guest, offers[0])), "Redeemed");
        using (var limited = await ClaimAsync(host, guest, offers[1]))
        {
            Assert.Equal("limit_reached", await CodeAsync(host, limited));
        }

        var family = await host.LoginAsync("family@example.az");
        await VerifyAsync(host, "family@example.az", veteran);
        for (var i = 0; i < 3; i++)
        {
            await SetStatusAsync(host, Id(await ClaimOkAsync(host, family, offers[i])), "Redeemed");
        }

        using (var fourth = await ClaimAsync(host, family, offers[3]))
        {
            Assert.Equal("limit_reached", await CodeAsync(host, fourth));
        }
    }

    [Fact]
    public async Task Expired_and_cancelled_reservations_do_not_count_and_a_stale_reservation_is_released_on_the_next_claim()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var tea = Id(await DailyAsync(host, shop, everyone, quota: 1));
        var guest = await host.LoginAsync("guest@example.az");

        var claim = await ClaimOkAsync(host, guest, tea);
        Assert.Equal(1, (await OfferDocAsync(host, tea))["usedToday"].AsInt32);

        // Rezervin müddəti keçdi (job hələ işləməyib): növbəti claim onu buraxır, vahid geri qayıdır və yenidən götürülür.
        await host.Database.GetCollection<BsonDocument>("claims").UpdateOneAsync(
            new BsonDocument("_id", Bin(Id(claim))),
            new BsonDocument("$set", new BsonDocument("expiresAt", host.Clock.UtcNow.AddMinutes(-1).UtcDateTime)), cancellationToken: Ct);

        var renewed = await ClaimOkAsync(host, guest, tea);
        Assert.NotEqual(Id(claim), Id(renewed));
        Assert.Equal(1, (await OfferDocAsync(host, tea))["usedToday"].AsInt32);

        var old = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(Id(claim)))).SingleAsync(Ct);
        Assert.Equal("Expired", old["status"].AsString);
    }

    private static Task SetStatusAsync(ApiTestHost host, string claimId, string status) =>
        host.Database.GetCollection<BsonDocument>("claims").UpdateOneAsync(
            new BsonDocument("_id", Bin(claimId)), new BsonDocument("$set", new BsonDocument("status", status)), cancellationToken: Ct);
}
