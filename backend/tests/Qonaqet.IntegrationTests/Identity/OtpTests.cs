using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary>Email OTP göndərmə və yoxlama (E02-T01).</summary>
[Collection(MongoCollection.Name)]
public sealed class OtpTests(MongoFixture mongo)
{
    private const string Email = "leyla@example.az";

    [Fact]
    public async Task Email_code_flow_registers_a_new_user_then_logs_in_the_same_user()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        var first = await host.LoginAsync("Leyla@Example.AZ");
        var second = await host.LoginAsync(Email);

        Assert.True(first.GetProperty("isNewUser").GetBoolean());
        Assert.False(first.GetProperty("profileCompleted").GetBoolean());
        Assert.False(second.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(first.GetProperty("userId").GetString(), second.GetProperty("userId").GetString());

        var user = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Email, user["email"].AsString);
        Assert.Equal("User", user["roles"].AsBsonArray.Single().AsString);
    }

    [Fact]
    public async Task Code_has_six_digits_and_only_its_hash_is_stored()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        using var send = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });

        Assert.Equal(HttpStatusCode.NoContent, send.StatusCode);
        var code = host.Otp.LastCode(Email);
        Assert.Matches("^[0-9]{6}$", code);
        var stored = await host.Database.GetCollection<BsonDocument>("otpCodes").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(code, stored.ToJson(), StringComparison.Ordinal);
        Assert.Equal(64, stored["codeHash"].AsString.Length);
        Assert.Equal(host.Clock.UtcNow.AddMinutes(10).UtcDateTime, stored["expiresAt"].ToUniversalTime());
    }

    [Fact]
    public async Task Code_expires_after_ten_minutes()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        using var send = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });

        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(10);
        using var verify = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = host.Otp.LastCode(Email) });

        Assert.Equal("otp_invalid", (await host.JsonAsync(verify)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Five_wrong_attempts_burn_the_code()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        using var send = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
        var code = host.Otp.LastCode(Email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            using var attempt = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = wrong });
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        using var correct = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code });

        Assert.Equal("otp_invalid", (await host.JsonAsync(correct)).GetProperty("code").GetString());
        var stored = await host.Database.GetCollection<BsonDocument>("otpCodes").Find(new BsonDocument()).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, stored["attempts"].AsInt32);
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await host.LoginAsync(Email);

        using var replay = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = host.Otp.LastCode(Email) });

        Assert.Equal("otp_invalid", (await host.JsonAsync(replay)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task At_most_three_sends_per_fifteen_minutes()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        for (var i = 0; i < 3; i++)
        {
            using var ok = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        }

        using var limited = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("otp_send_limit", (await host.JsonAsync(limited)).GetProperty("code").GetString());

        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(15).AddSeconds(1);
        using var later = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
        Assert.Equal(HttpStatusCode.NoContent, later.StatusCode);
        Assert.Equal(4, host.Otp.Sent);
    }

    [Fact]
    public async Task Response_does_not_reveal_whether_the_email_has_an_account()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await host.LoginAsync(Email);

        using var known = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
        using var unknown = await host.PostAsync("/api/v1/auth/otp/send", new { email = "nobody@example.az" });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Otp_endpoints_are_rate_limited()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        host.RateLimits.Deny = true;

        using var send = await host.PostAsync("/api/v1/auth/otp/send", new { email = Email });
        using var verify = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = "123456" });

        Assert.Equal(HttpStatusCode.TooManyRequests, send.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, verify.StatusCode);
        Assert.Equal("rate_limited", (await host.JsonAsync(send)).GetProperty("code").GetString());
        Assert.Equal(0, host.Otp.Sent);
    }

    [Fact]
    public async Task Invalid_email_and_code_format_fail_validation()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        using var badEmail = await host.PostAsync("/api/v1/auth/otp/send", new { email = "not-an-email" });
        using var badCode = await host.PostAsync("/api/v1/auth/otp/verify", new { email = Email, code = "12ab" });

        Assert.Equal("validation_failed", (await host.JsonAsync(badEmail)).GetProperty("code").GetString());
        Assert.Equal("validation_failed", (await host.JsonAsync(badCode)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task Code_is_logged_only_in_development(string environment, bool logged)
    {
        var logger = new CapturingLogger<LoggingOtpSender>();
        var sender = new LoggingOtpSender(new TestEnvironment(environment), logger);

        await sender.SendAsync(Email, "482913", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(logged, logger.Messages.Any(m => m.Contains("482913", StringComparison.Ordinal)));
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add(formatter(state, exception));
    }
}
