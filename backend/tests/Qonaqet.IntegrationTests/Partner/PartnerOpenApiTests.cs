using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// "QONAQET Partner API v1" OpenAPI sənədi (E13-T10): bütün partner endpoint-ləri, OAuth2 client credentials sxemi və POST-larda
/// <c>Idempotency-Key</c>. <c>QONAQET_OPENAPI_OUT</c> verilibsə sənəd həmin qovluğa yazılır — CI onu artefakt kimi dərc edir.
/// </summary>
public sealed class PartnerOpenApiTests
{
    [Fact]
    public async Task Partner_document_describes_auth_idempotency_and_every_endpoint()
    {
        await using var factory = new WebApplicationFactory<Integration.Api.Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(new Uri("/openapi/partner-v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("QONAQET Partner API v1", root.GetProperty("info").GetProperty("title").GetString());
        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        foreach (var expected in new[]
                 {
                     "/oauth/token", "/partner/v1/codes/{code}", "/partner/v1/redemptions", "/partner/v1/redemptions/{redemptionId}/reverse",
                     "/partner/v1/catalog/{businessId}", "/partner/v1/gifts", "/partner/v1/balance", "/partner/v1/webhooks",
                     "/partner/v1/webhooks/deliveries/{deliveryId}/redeliver", "/partner/v1/sandbox/test-codes",
                 })
        {
            Assert.Contains(expected, paths);
        }

        var flow = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("oauth2").GetProperty("flows").GetProperty("clientCredentials");
        Assert.Equal("/oauth/token", flow.GetProperty("tokenUrl").GetString());
        Assert.True(flow.GetProperty("scopes").TryGetProperty("redemptions:write", out _));

        var redeem = root.GetProperty("paths").GetProperty("/partner/v1/redemptions").GetProperty("post");
        Assert.Contains(redeem.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
        var token = root.GetProperty("paths").GetProperty("/oauth/token").GetProperty("post");
        Assert.False(token.TryGetProperty("parameters", out var tokenParams)
            && tokenParams.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "Idempotency-Key"));

        if (Environment.GetEnvironmentVariable("QONAQET_OPENAPI_OUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "partner-v1.json"), json, TestContext.Current.CancellationToken);
        }
    }
}
