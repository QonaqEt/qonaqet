using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Claim QR token-i və qısa kod (E06-T04).</summary>
[Collection(MongoCollection.Name)]
public sealed class ClaimCodeTests(MongoFixture mongo)
{
    private static RedemptionCodes Codes(string secret = "test-qr-secret-0123456789abcdef") =>
        new(Options.Create(new SecurityOptions { QrSigningSecret = secret }), new TestEnvironment());

    [Fact]
    public void Qr_payload_is_base64url_of_prefix_id_and_hmac_and_cannot_be_forged()
    {
        var codes = Codes();
        var id = Guid.CreateVersion7();
        var payload = codes.QrPayload(RedemptionCodeKind.Claim, id);

        Assert.DoesNotContain('=', payload);
        Assert.DoesNotContain('+', payload);
        var raw = Encoding.ASCII.GetString(Convert.FromBase64String(Pad(payload.Replace('-', '+').Replace('_', '/'))));
        Assert.StartsWith($"c.{id:N}.", raw, StringComparison.Ordinal);
        Assert.Equal(new RedemptionCodeRef(RedemptionCodeKind.Claim, id), codes.ParseQr(payload));
        Assert.Equal(RedemptionCodeKind.Gift, codes.ParseQr(codes.QrPayload(RedemptionCodeKind.Gift, id))!.Kind);

        // Başqa ID ilə eyni imza, başqa açar, pozulmuş simvol və boş dəyər qəbul olunmur.
        var forged = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw.Replace(id.ToString("N"), Guid.CreateVersion7().ToString("N"), StringComparison.Ordinal)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(codes.ParseQr(forged));
        Assert.Null(Codes("another-secret-0123456789abcdef").ParseQr(payload));
        Assert.Null(codes.ParseQr(payload[..^2] + (payload[^2] == 'A' ? "B" : "A") + payload[^1]));
        Assert.Null(codes.ParseQr(""));
        Assert.Null(codes.ParseQr("not a code"));
    }

    [Fact]
    public void Short_code_is_readable_deterministic_and_normalized_on_input()
    {
        var codes = Codes();
        var id = Guid.CreateVersion7();
        var code = codes.ShortCode(RedemptionCodeKind.Claim, id);

        Assert.Equal(8, code.Length);
        Assert.All(code, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
        Assert.Equal(code, codes.ShortCode(RedemptionCodeKind.Claim, id));
        Assert.NotEqual(code, codes.ShortCode(RedemptionCodeKind.Gift, id));
        Assert.NotEqual(code, Codes("another-secret-0123456789abcdef").ShortCode(RedemptionCodeKind.Claim, id));

        var hash = codes.ShortCodeHash(code);
        Assert.NotNull(hash);
        Assert.Equal(hash, codes.ShortCodeHash($" {code[..4].ToLowerInvariant()}-{code[4..]} "));
        Assert.Equal(codes.ShortCodeHash("0011ABCD"), codes.ShortCodeHash("oOiLabcd"));
        Assert.Null(codes.ShortCodeHash("ABC"));
        Assert.Null(codes.ShortCodeHash("ABCDEFGU"));
        Assert.Null(codes.ShortCodeHash(null));
    }

    [Fact]
    public async Task Recipient_sees_codes_only_while_the_reservation_is_valid_and_codes_are_never_stored_or_logged()
    {
        mongo.SkipIfUnavailable();
        var logs = new LogCapture();
        await using var host = new ApiTestHost(mongo, configure: s => s.AddSingleton<ILoggerProvider>(logs));
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var tea = Id(await DailyAsync(host, shop, everyone, quota: 2));
        var guest = await host.LoginAsync("guest@example.az");

        var claim = await ClaimOkAsync(host, guest, tea);
        var codes = claim.GetProperty("codes");
        var payload = codes.GetProperty("qrPayload").GetString()!;
        var shortCode = codes.GetProperty("shortCode").GetString()!;
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}$", shortCode);
        var qr = codes.GetProperty("qr");
        Assert.Equal(qr.GetProperty("size").GetInt32(), qr.GetProperty("rows").GetArrayLength());

        var parsed = host.Services.GetRequiredService<IRedemptionCodes>().ParseQr(payload);
        Assert.Equal(Guid.Parse(Id(claim)), parsed!.Id);

        using (var mine = await host.GetAsync($"/api/v1/me/claims/{Id(claim)}", Access(guest)))
        {
            Assert.Equal(payload, (await host.JsonAsync(mine)).GetProperty("codes").GetProperty("qrPayload").GetString());
        }

        // Kod (nə QR, nə qısa kod) DB-də saxlanılmır, yalnız qısa kodun hash-i; heç bir loga düşmür.
        var stored = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(Id(claim)))).SingleAsync(Ct);
        var json = stored.ToJson();
        Assert.DoesNotContain(payload, json, StringComparison.Ordinal);
        Assert.DoesNotContain(shortCode.Replace("-", "", StringComparison.Ordinal), json, StringComparison.Ordinal);
        Assert.Equal(64, stored["shortCodeHash"].AsString.Length);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(payload, StringComparison.Ordinal) || l.Contains(shortCode[..4] + "-", StringComparison.Ordinal));

        // Başqası bu claim-i görmür; təhvil verilmiş claim-in kodu göstərilmir.
        var other = await host.LoginAsync("other@example.az");
        using (var foreign = await host.GetAsync($"/api/v1/me/claims/{Id(claim)}", Access(other)))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        await host.Database.GetCollection<BsonDocument>("claims").UpdateOneAsync(
            new BsonDocument("_id", Bin(Id(claim))), new BsonDocument("$set", new BsonDocument("status", "Redeemed")), cancellationToken: Ct);
        using (var list = await host.GetAsync("/api/v1/me/claims", Access(guest)))
        {
            var item = (await host.JsonAsync(list)).EnumerateArray().Single();
            Assert.Equal("Redeemed", item.GetProperty("status").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("codes").ValueKind);
        }
    }

    [Fact]
    public async Task Claim_endpoints_are_rate_limited()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var guest = await host.LoginAsync("guest@example.az");

        host.RateLimits.Deny = true;
        using var claim = await ClaimAsync(host, guest, Guid.CreateVersion7().ToString());
        using var list = await host.GetAsync("/api/v1/me/claims", Access(guest));
        host.RateLimits.Deny = false;

        Assert.Equal(HttpStatusCode.TooManyRequests, claim.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, list.StatusCode);
    }

    private static string Pad(string value) => value + new string('=', (4 - (value.Length % 4)) % 4);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "Qonaqet.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
