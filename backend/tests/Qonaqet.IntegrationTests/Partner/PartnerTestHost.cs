using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Real Partner API host-u (Integration.Api), Api test host-u ilə <b>eyni</b> Mongo və Postgres bazalarına qoşulur (migrasiyaları Api host-u
/// artıq tətbiq edib). Saat Api host-u ilə paylaşılır; rate limit store yaddaşdadır.
/// </summary>
public sealed class PartnerTestHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Qonaqet.Integration.Api.Program> _factory;

    public PartnerTestHost(ApiTestHost api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _factory = new WebApplicationFactory<Qonaqet.Integration.Api.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Mongo:ConnectionString", api.MongoConnectionString);
            b.UseSetting("Mongo:Database", api.DatabaseName);
            b.UseSetting("Mongo:ApplyMigrationsOnStartup", "false");
            b.UseSetting("Postgres:ApplyMigrationsOnStartup", "false");
            if (api.PostgresConnectionString is not null)
            {
                b.UseSetting("ConnectionStrings:Postgres", api.PostgresConnectionString);
            }

            b.UseSetting("Security:TokenHashSecret", "test-token-hash-secret-0123456789abcdef");
            b.UseSetting("Jwt:SigningKey", "test-jwt-signing-key-0123456789abcdef0123456789");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IRateLimitStore>();
                s.AddSingleton<IRateLimitStore>(RateLimits);
                s.RemoveAll<IClock>();
                s.AddSingleton<IClock>(api.Clock);
                s.AddControllers().AddApplicationPart(typeof(PartnerTestHost).Assembly);
            });
        });
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    public CountingRateLimitStore RateLimits { get; } = new();

    public IServiceProvider Services => _factory.Services;

    /// <summary>Client credentials ilə token (form-urlencoded, RFC 6749).</summary>
    public async Task<HttpResponseMessage> TokenAsync(string clientId, string clientSecret, string? scope = null)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret };
        if (scope is not null)
        {
            form["scope"] = scope;
        }

        using var content = new FormUrlEncodedContent(form);
        return await Client.PostAsync(new Uri("/oauth/token", UriKind.Relative), content, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AccessTokenAsync(string clientId, string clientSecret, string? scope = null)
    {
        using var response = await TokenAsync(clientId, clientSecret, scope).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        return body.GetProperty("access_token").GetString()!;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Sayan rate limit store-u (partition başına pəncərə sayğacı): client limitini yoxlamaq üçün.</summary>
public sealed class CountingRateLimitStore : IRateLimitStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    public Task<RateLimitDecision> TryAcquireAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken)
    {
        var count = _counts.AddOrUpdate(partitionKey, 1, (_, c) => c + 1);
        return Task.FromResult(count <= permitLimit
            ? new RateLimitDecision(true, permitLimit - count, TimeSpan.Zero)
            : new RateLimitDecision(false, 0, window));
    }
}
