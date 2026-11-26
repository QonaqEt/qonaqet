using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Qonaqet.IntegrationTests;

/// <summary>Hər iki HTTP host-u konfiqurasiya xətası olmadan qalxır (E01-T01).</summary>
public sealed class HostStartupTests
{
    [Fact]
    public async Task Api_host_starts()
    {
        await using var factory = new WebApplicationFactory<Api.Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/__unknown", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Integration_api_host_starts()
    {
        await using var factory = new WebApplicationFactory<Integration.Api.Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/__unknown", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Partner_routes_are_served_only_by_the_integration_host()
    {
        await using var partner = new WebApplicationFactory<Integration.Api.Program>();
        await using var api = new WebApplicationFactory<Api.Program>();
        var ct = TestContext.Current.CancellationToken;

        using var status = await partner.CreateClient().GetAsync(new Uri("/partner/v1/status", UriKind.Relative), ct);
        using var live = await partner.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), ct);
        using var onApi = await api.CreateClient().GetAsync(new Uri("/partner/v1/status", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("\"version\":\"v1\"", await status.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, onApi.StatusCode);
    }
}
