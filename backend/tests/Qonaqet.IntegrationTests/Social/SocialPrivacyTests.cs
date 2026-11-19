using System.Text.Json;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>
/// Sosial qonaqlığın məxfilik zəmanətləri (E06-T07): biznes alıcını tanımır, həssas kateqoriya adı işçiyə görünmür, ianəçi yalnız
/// aqreqat görür. Bu sinif ci-backend-in integration addımında hər PR-da işləyir (Testcontainers Mongo); sınaq uğursuzdursa merge olmur.
/// </summary>
[Collection(MongoCollection.Name)]
[Trait("Category", "Privacy")]
public sealed class SocialPrivacyTests(MongoFixture mongo)
{
    private const string RecipientEmail = "leyla.recipient@example.az";
    private const string FirstName = "Leylagül";
    private const string LastName = "Məmmədzadə";

    [Fact]
    public async Task Business_responses_never_contain_the_recipients_name_email_or_id()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var tea = Id(await DailyAsync(host, shop, everyone, quota: 5));
        var (recipient, claim) = await NamedRecipientClaimAsync(host, tea);
        var code = claim.GetProperty("codes").GetProperty("qrPayload").GetString()!;
        var secrets = new[] { RecipientEmail, FirstName, LastName, UserId(recipient) };

        var bodies = new List<string>
        {
            await RawAsync(host, HttpMethod.Get, $"/api/v1/business/{shop.BusinessId}/offers", shop.Staff),
            await RawAsync(host, HttpMethod.Get, $"/api/v1/business/{shop.BusinessId}/offers", shop.Owner),
            await RawAsync(host, HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem/preview", shop.Staff, new { code }),
            await RawAsync(host, HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", shop.Staff, new { code }),
            await RawAsync(host, HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", shop.Staff, new { code }),
            await RawAsync(host, HttpMethod.Get, $"/api/v1/offers?businessId={shop.BusinessId}", null),
        };

        foreach (var body in bodies)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Cavabda "recipient" adlı heç bir sahə də yoxdur.
        Assert.All(bodies, b => Assert.DoesNotContain("recipient", b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Staff_sees_the_neutral_label_for_sensitive_categories_never_the_category_name()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var veteran = await CategoryAsync(host, "veteran-family");
        var veteranName = await CategoryNameAsync(host, "veteran-family");
        var student = await CategoryAsync(host, "student");
        var soup = Id(await DailyAsync(host, shop, veteran, quota: 5, title: "Şorba"));
        var tea = Id(await DailyAsync(host, shop, student, quota: 5));

        var family = await host.LoginAsync("family@example.az");
        await VerifyAsync(host, "family@example.az", veteran);
        var claim = await ClaimOkAsync(host, family, soup);
        var code = claim.GetProperty("codes").GetProperty("shortCode").GetString()!;

        var preview = await RawAsync(host, HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem/preview", shop.Staff, new { code });
        var redeem = await RawAsync(host, HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", shop.Staff, new { code });
        var offers = await RawAsync(host, HttpMethod.Get, $"/api/v1/business/{shop.BusinessId}/offers", shop.Staff);

        foreach (var body in new[] { preview, redeem })
        {
            Assert.Equal("Qonaq", JsonDocument.Parse(body).RootElement.GetProperty("label").GetString());
            Assert.DoesNotContain(veteranName, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("veteran", body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(veteranName, offers, StringComparison.OrdinalIgnoreCase);
        var labels = JsonDocument.Parse(offers).RootElement.EnumerateArray().ToDictionary(o => o.GetProperty("id").GetString()!, o => o.GetProperty("categoryLabel").GetString());
        Assert.Equal("Qonaq", labels[soup]);
        Assert.Equal(await CategoryNameAsync(host, "student"), labels[tea]);
    }

    [Fact]
    public async Task Donor_sees_only_aggregates_never_who_was_treated()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var donor = await host.LoginAsync("donor@example.az");
        var coffee = Id(await SuspendedAsync(host, shop, everyone, quantity: 3, donorEmail: "donor@example.az", display: "Named", donorName: "Arzu"));
        var (recipient, claim) = await NamedRecipientClaimAsync(host, coffee);
        using (await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", Access(shop.Staff),
            new { code = claim.GetProperty("codes").GetProperty("qrPayload").GetString() }))
        {
        }

        var donations = await RawAsync(host, HttpMethod.Get, "/api/v1/me/donations", donor);
        var item = JsonDocument.Parse(donations).RootElement.EnumerateArray().Single();
        Assert.Equal(3, item.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(2, item.GetProperty("remaining").GetInt32());
        Assert.Equal(1, item.GetProperty("redeemed").GetInt64());
        foreach (var secret in new[] { RecipientEmail, FirstName, LastName, UserId(recipient), Id(claim) })
        {
            Assert.DoesNotContain(secret, donations, StringComparison.OrdinalIgnoreCase);
        }

        // Alıcı ianəçinin seçdiyi adı görür; anonim ianəçinin adı isə heç yerdə yoxdur.
        Assert.Equal("Arzu sizə ısmarladı 💚", claim.GetProperty("donorDisplayText").GetString());
        var anonymous = Id(await SuspendedAsync(host, shop, everyone, quantity: 1, display: "Anonymous", donorName: "Gizli Ad"));
        var publicList = await RawAsync(host, HttpMethod.Get, $"/api/v1/offers?businessId={shop.BusinessId}", null);
        Assert.DoesNotContain("Gizli Ad", publicList, StringComparison.Ordinal);
        Assert.Contains("Bir xeyirxah insan ısmarlayır", publicList, StringComparison.Ordinal);
        Assert.DoesNotContain(UserId(donor), publicList, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(anonymous);
    }

    private static async Task<(JsonElement Recipient, JsonElement Claim)> NamedRecipientClaimAsync(ApiTestHost host, string offerId)
    {
        var recipient = await host.LoginAsync(RecipientEmail);
        using (var profile = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", Access(recipient), new { firstName = FirstName, lastName = LastName }))
        {
            Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync(Ct));
        }

        return (recipient, await ClaimOkAsync(host, recipient, offerId));
    }

    private static async Task<string> RawAsync(ApiTestHost host, HttpMethod method, string path, JsonElement? login, object? body = null)
    {
        using var response = await host.SendAsync(method, path, login is { } l ? Access(l) : null, body);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<string> CategoryNameAsync(ApiTestHost host, string code)
    {
        using var response = await host.GetAsync("/api/v1/categories", null);
        return (await host.JsonAsync(response)).EnumerateArray().Single(c => c.GetProperty("code").GetString() == code).GetProperty("name").GetString()!;
    }
}
