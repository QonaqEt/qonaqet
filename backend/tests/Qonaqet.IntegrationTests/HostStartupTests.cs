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
}
