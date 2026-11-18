using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Web;
using Qonaqet.IntegrationTests.Messaging;

namespace Qonaqet.IntegrationTests.Web;

/// <summary>Health endpoint-ləri və correlation id real store-lara qarşı (E01-T12).</summary>
[Collection(MessagingCollection.Name)]
public sealed class ObservabilityTests(MessagingFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_is_up_and_readiness_checks_every_store_with_valkey_only_degrading()
    {
        fixture.SkipIfUnavailable();
        await using var factory = await CreateFactoryAsync(valkey: "127.0.0.1:1");
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Degraded", body.RootElement.GetProperty("status").GetString());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("mongo").GetString());
        Assert.Equal("Healthy", checks.GetProperty("postgres").GetString());
        Assert.Equal("Healthy", checks.GetProperty("rabbitmq").GetString());
        Assert.Equal("Degraded", checks.GetProperty("valkey").GetString());
    }

    [Fact]
    public async Task Readiness_fails_when_a_primary_store_is_down()
    {
        fixture.SkipIfUnavailable();
        await using var factory = await CreateFactoryAsync(valkey: "127.0.0.1:1", postgres: "Host=127.0.0.1;Port=1;Database=x;Timeout=1");
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain("Exception", await ready.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_or_generated()
    {
        await using var factory = new WebApplicationFactory<Api.Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");
        using var echoed = await client.SendAsync(request, Ct);
        Assert.Equal("abc-123", echoed.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        using var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        unsafeRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, "<script>");
        using var generated = await client.SendAsync(unsafeRequest, Ct);
        var id = generated.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual("<script>", id);
        Assert.True(CorrelationIdMiddleware.IsValid(id));
    }

    private async Task<WebApplicationFactory<Api.Program>> CreateFactoryAsync(string valkey, string? postgres = null)
    {
        var pg = postgres ?? await fixture.Postgres.NewDatabaseAsync();
        return new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b => b
            .UseSetting("Mongo:ConnectionString", fixture.Mongo.ConnectionString + "&serverSelectionTimeoutMS=2000")
            .UseSetting("ConnectionStrings:Postgres", pg)
            .UseSetting("Valkey:ConnectionString", valkey)
            .UseSetting("RabbitMq:Uri", fixture.RabbitUri.ToString()));
    }
}

/// <summary>Log maskalanması (Docker tələb etmir).</summary>
public sealed class RedactingFormatterTests
{
    [Theory]
    [InlineData("OtpCode", true)]
    [InlineData("refresh_token", true)]
    [InlineData("Password", true)]
    [InlineData("fin", true)]
    [InlineData("Email", true)]
    [InlineData("Iban", true)]
    [InlineData("GiftId", false)]
    [InlineData("Finished", false)]
    [InlineData("StatusCode", false)]
    public void Sensitive_names(string name, bool sensitive) => Assert.Equal(sensitive, RedactingJsonConsoleFormatter.IsSensitive(name));

    [Fact]
    public void Sensitive_values_never_reach_the_output()
    {
        var formatter = new RedactingJsonConsoleFormatter();
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Email", "leyla@example.az"),
            new("Code", "482913"),
            new("GiftId", "g-1"),
            new("{OriginalFormat}", "OTP {Code} sent to {Email} for {GiftId}"),
        };
        using var writer = new StringWriter();

        formatter.Write(
            new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(LogLevel.Information, "test", new EventId(7), state, null, (s, _) => "unused"),
            new LoggerExternalScopeProvider(),
            writer);

        var line = writer.ToString();
        Assert.DoesNotContain("482913", line, StringComparison.Ordinal);
        Assert.DoesNotContain("leyla@example.az", line, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(line);
        Assert.Equal("OTP *** sent to *** for g-1", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("g-1", json.RootElement.GetProperty("state").GetProperty("GiftId").GetString());
        Assert.Equal(RedactingJsonConsoleFormatter.Mask, json.RootElement.GetProperty("state").GetProperty("Code").GetString());
    }
}
