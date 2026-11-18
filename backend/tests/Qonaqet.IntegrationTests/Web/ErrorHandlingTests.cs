using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Web;

/// <summary>OpenAPI və ProblemDetails davranışı (E01-T13).</summary>
public sealed class ErrorHandlingTests
{
    [Fact]
    public async Task Unhandled_exception_in_production_returns_code_without_internals()
    {
        await using var factory = Factory<Api.Program>("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/__test/throw", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("internal_error", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        foreach (var leak in new[] { "SecretFin5ABC123", "InvalidOperationException", "TestController", "   at " })
        {
            Assert.DoesNotContain(leak, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Development_adds_exception_summary_but_never_a_stack_trace()
    {
        await using var factory = Factory<Api.Program>("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/__test/throw", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_failure_maps_to_problem_details_with_its_stable_code()
    {
        await using var factory = Factory<Api.Program>("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/__test/expired", UriKind.Relative), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("gift_expired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_body_returns_validation_failed_with_field_errors()
    {
        await using var factory = Factory<Api.Program>("Production");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/__test/echo", UriKind.Relative), new { name = "", amount = "abc" }, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.NotEmpty(problem.GetProperty("errors").EnumerateArray());
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task OpenApi_document_is_served_only_in_development(string environment, HttpStatusCode expected)
    {
        await using var api = Factory<Api.Program>(environment);
        await using var partner = Factory<Integration.Api.Program>(environment);

        using var apiDoc = await api.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using var partnerDoc = await partner.CreateClient().GetAsync(new Uri("/openapi/partner-v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, apiDoc.StatusCode);
        Assert.Equal(expected, partnerDoc.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var title = (await partnerDoc.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("info").GetProperty("title").GetString();
            Assert.Equal("QONAQET Partner API v1", title);
        }
    }

    private static WebApplicationFactory<TEntry> Factory<TEntry>(string environment)
        where TEntry : class =>
        new WebApplicationFactory<TEntry>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureServices(s => s.AddControllers().AddApplicationPart(typeof(TestController).Assembly));
        });
}

[ApiController]
[Route("__test")]
public sealed class TestController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("FIN SecretFin5ABC123 sızmamalıdır");

    [HttpGet("expired")]
    public IActionResult Expired() => Result.Failure(Error.Failure("gift_expired", "Hədiyyənin vaxtı bitib.")).ToActionResult();

    [HttpPost("echo")]
    public IActionResult Echo(EchoRequest request) => Result<EchoRequest>.Success(request).ToActionResult();

    public sealed record EchoRequest([property: Required, MinLength(1)] string Name, int Amount);
}
