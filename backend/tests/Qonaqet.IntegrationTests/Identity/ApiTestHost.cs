using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>
/// Real Api host-u (bütün pipeline, controller-lər, auth) Testcontainers Mongo-suna qarşı. Hər host öz təmiz
/// verilənlər bazasını alır. Valkey əvəzinə yaddaşda rate limit store, email əvəzinə kodu tutan göndərici.
/// </summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Api.Program> _factory;

    public ApiTestHost(MongoFixture mongo, string environment = "Development", Action<IServiceCollection>? configure = null, string? postgres = null)
    {
        ArgumentNullException.ThrowIfNull(mongo);
        DatabaseName = "t_" + Guid.NewGuid().ToString("N")[..12];
        Database = mongo.Client.GetDatabase(DatabaseName);
        _factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Mongo:ConnectionString", mongo.ConnectionString);
            b.UseSetting("Mongo:Database", DatabaseName);
            b.UseSetting("Mongo:ApplyMigrationsOnStartup", "true");
            // Postgres yalnız ötürüləndə (ödəniş, ledger testləri): host startda migrasiyaları tətbiq edir.
            b.UseSetting("Postgres:ApplyMigrationsOnStartup", postgres is null ? "false" : "true");
            if (postgres is not null)
            {
                b.UseSetting("ConnectionStrings:Postgres", postgres);
            }

            b.UseSetting("Security:TokenHashSecret", "test-token-hash-secret-0123456789abcdef");
            b.UseSetting("Jwt:SigningKey", "test-jwt-signing-key-0123456789abcdef0123456789");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IOtpSender>();
                s.AddSingleton<IOtpSender>(Otp);
                s.RemoveAll<IRateLimitStore>();
                s.AddSingleton<IRateLimitStore>(RateLimits);
                s.RemoveAll<IClock>();
                s.AddSingleton<IClock>(Clock);
                s.AddControllers().AddApplicationPart(typeof(ApiTestHost).Assembly);
                configure?.Invoke(s);
            });
        });
        Client = _factory.CreateClient();
    }

    public string DatabaseName { get; }

    public IMongoDatabase Database { get; }

    public HttpClient Client { get; }

    public CapturingOtpSender Otp { get; } = new();

    public InMemoryRateLimitStore RateLimits { get; } = new();

    /// <summary>Real vaxtdan başlayır: JWT doğrulaması sistem saatı ilə işləyir.</summary>
    public FixedClock Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    public IServiceProvider Services => _factory.Services;

    public Task<HttpResponseMessage> PostAsync(string path, object body) =>
        Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, TestContext.Current.CancellationToken);

    public async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    /// <summary>OTP göndərir və tutulan kodla yoxlayır; verify cavabını qaytarır.</summary>
    public async Task<JsonElement> LoginAsync(string email)
    {
        using (var send = await PostAsync("/api/v1/auth/otp/send", new { email }))
        {
            send.EnsureSuccessStatusCode();
        }

        using var verify = await PostAsync("/api/v1/auth/otp/verify", new { email, code = Otp.LastCode(email) });
        Assert.True(verify.IsSuccessStatusCode, await verify.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await JsonAsync(verify);
    }

    /// <summary>Bearer token (varsa) və JSON body (varsa) ilə istənilən metod.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
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

        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bearer token ilə GET.</summary>
    public async Task<HttpResponseMessage> GetAsync(string path, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class CapturingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<string, string> _codes = new(StringComparer.OrdinalIgnoreCase);

    public int Sent { get; private set; }

    public Task SendAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        _codes[email] = code;
        Sent++;
        return Task.CompletedTask;
    }

    public string LastCode(string email) => _codes[email];
}

/// <summary>Valkey-siz rate limit: limiti testlər <see cref="Deny"/> ilə idarə edir.</summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    public bool Deny { get; set; }

    public Task<RateLimitDecision> TryAcquireAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken) =>
        Task.FromResult(Deny ? new RateLimitDecision(false, 0, TimeSpan.FromSeconds(30)) : new RateLimitDecision(true, permitLimit, TimeSpan.Zero));
}

/// <summary>Yalnız testlər üçün: autentifikasiya olunmuş principal-ı göstərir.</summary>
[ApiController]
[Route("__test/auth")]
public sealed class AuthProbeController : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI() => User.Identity?.IsAuthenticated == true
        ? Ok(new { sub = User.FindFirst("sub")?.Value, roles = User.FindAll("role").Select(c => c.Value).ToArray() })
        : Unauthorized();
}
