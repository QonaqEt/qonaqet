using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Email;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Email;

/// <summary>Mailpit (dev SMTP) Testcontainers ilə; image compose-dakı ilə eyni pinlənib.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.33.1";

    private IContainer? _container;

    public string? UnavailableReason { get; private set; }

    public string Host { get; private set; } = string.Empty;

    public int SmtpPort { get; private set; }

    public HttpClient Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(1025, true)
                .WithPortBinding(8025, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/readyz")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
            return;
        }

        Host = _container.Hostname;
        SmtpPort = _container.GetMappedPublicPort(1025);
        Api = new HttpClient { BaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(8025)}") };
    }

    public SmtpOptions Smtp() => new() { Host = Host, Port = SmtpPort, Security = "None" };

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    /// <summary>Bu ünvana gələn son məktubun tam məzmunu (gələnə qədər gözləyir).</summary>
    public async Task<JsonElement> LatestMessageToAsync(string address)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 100; i++)
        {
            var search = await Api.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString("to:" + address)}", ct);
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                return await Api.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}", ct);
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"Mailpit-ə məktub gəlmədi: {address}");
    }

    public async ValueTask DisposeAsync()
    {
        Api?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>, ICollectionFixture<MongoFixture>
{
    public const string Name = "mailpit";
}

/// <summary>Email göndərmə real SMTP-yə (Mailpit) qarşı (E03-T03).</summary>
[Collection(MailpitCollection.Name)]
public sealed class MailpitTests(MailpitFixture mailpit, MongoFixture mongo)
{
    [Fact]
    public async Task Templated_email_arrives_with_correct_azerbaijani_letters()
    {
        mailpit.SkipIfUnavailable();
        var sender = new MailKitEmailSender(Microsoft.Extensions.Options.Options.Create(mailpit.Smtp()));
        var rendered = new EmailTemplateRenderer().Render("otp", new Dictionary<string, string?>
        {
            ["code"] = "731904",
            ["validMinutes"] = "10",
            ["expiresAtBaku"] = "18:05",
        });

        await sender.SendAsync(rendered.To("şəhla.ağayeva@example.az"), TestContext.Current.CancellationToken);

        var message = await mailpit.LatestMessageToAsync("şəhla.ağayeva@example.az");
        Assert.Equal("QONAQET giriş kodunuz: 731904", message.GetProperty("Subject").GetString());
        var html = message.GetProperty("HTML").GetString()!;
        var text = message.GetProperty("Text").GetString()!;
        Assert.Contains("Qonaq et, xoşbəxt et", html, StringComparison.Ordinal);
        Assert.Contains("dəqiqə etibarlıdır", html, StringComparison.Ordinal);
        Assert.Contains("Kodu heç kimlə paylaşmayın", text, StringComparison.Ordinal);
        Assert.Equal("QONAQET", message.GetProperty("From").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Otp_request_sends_the_login_code_by_email()
    {
        mailpit.SkipIfUnavailable();
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo, configure: s =>
        {
            s.RemoveAll<IOtpSender>();
            s.AddSingleton<IOtpSender, EmailOtpSender>();
            s.Configure<SmtpOptions>(o =>
            {
                o.Host = mailpit.Host;
                o.Port = mailpit.SmtpPort;
                o.Security = "None";
            });
        });

        using var response = await host.PostAsync("/api/v1/auth/otp/send", new { email = "otp.mail@example.az" });
        response.EnsureSuccessStatusCode();

        var message = await mailpit.LatestMessageToAsync("otp.mail@example.az");
        var subject = message.GetProperty("Subject").GetString()!;
        var code = subject[^6..];
        Assert.Matches("^[0-9]{6}$", code);

        using var verify = await host.PostAsync("/api/v1/auth/otp/verify", new { email = "otp.mail@example.az", code });
        Assert.True(verify.IsSuccessStatusCode, await verify.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Otp_request_succeeds_even_when_smtp_is_down()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo, configure: s =>
        {
            s.RemoveAll<IOtpSender>();
            s.AddSingleton<IOtpSender, EmailOtpSender>();
            s.Configure<SmtpOptions>(o =>
            {
                o.Host = "127.0.0.1";
                o.Port = 1;
            });
        });

        using var response = await host.PostAsync("/api/v1/auth/otp/send", new { email = "smtp.down@example.az" });

        Assert.True(response.IsSuccessStatusCode);
    }
}
