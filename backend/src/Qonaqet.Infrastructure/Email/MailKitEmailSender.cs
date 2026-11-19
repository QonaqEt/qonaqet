using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Email;

/// <summary><c>Smtp</c> bölməsi. Dev-də Mailpit (<c>localhost:1025</c>, TLS və giriş yoxdur).</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary><c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c>, <c>Auto</c>.</summary>
    public string Security { get; set; } = "None";

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@qonaqet.az";

    public string FromName { get; set; } = "QONAQET";
}

/// <summary><c>Email</c> bölməsi: növbənin yenidən cəhd fasilələri.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Hər uğursuz cəhddən sonrakı fasilə; siyahı bitəndə email imtina olunur (loglanır).</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; set; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];
}

/// <summary>MailKit SMTP göndəricisi. Başlıqlar və gövdə UTF-8-dir (Azərbaycan hərfləri: ə ğ ı ö ü ç ş).</summary>
public sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, Enum.Parse<SecureSocketOptions>(smtp.Security, ignoreCase: true), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Proses daxili növbə; <see cref="EmailDispatcher"/> boşaldır.</summary>
public sealed class ChannelEmailQueue : IEmailQueue
{
    private readonly Channel<QueuedEmail> _channel = Channel.CreateUnbounded<QueuedEmail>(new UnboundedChannelOptions { SingleReader = true });

    internal ChannelReader<QueuedEmail> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(EmailMessage message, string kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _channel.Writer.WriteAsync(new QueuedEmail(message, kind, Attempt: 1), cancellationToken);
    }

    internal bool TryRequeue(QueuedEmail email) => _channel.Writer.TryWrite(email);
}

internal sealed record QueuedEmail(EmailMessage Message, string Kind, int Attempt);

/// <summary>
/// Növbədəki email-ləri göndərir. Uğursuz cəhd <see cref="EmailOptions.RetryDelays"/> fasiləsi ilə yenidən növbəyə
/// düşür; çağıran əməliyyat (məs. OTP göndər) heç vaxt SMTP xətası görmür. Loglarda ünvan və məzmun yoxdur.
/// </summary>
public sealed partial class EmailDispatcher(
    ChannelEmailQueue queue, IEmailSender sender, IOptions<EmailOptions> options, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await DeliverAsync(email, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task DeliverAsync(QueuedEmail email, CancellationToken stoppingToken)
    {
        try
        {
            await sender.SendAsync(email.Message, stoppingToken).ConfigureAwait(false);
            LogSent(logger, email.Kind, email.Attempt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            var delays = options.Value.RetryDelays;
            if (email.Attempt > delays.Count)
            {
                LogGaveUp(logger, ex, email.Kind, email.Attempt);
                return;
            }

            var delay = delays[email.Attempt - 1];
            LogRetrying(logger, email.Kind, email.Attempt, delay, ex.GetType().Name);
            _ = RequeueLaterAsync(email with { Attempt = email.Attempt + 1 }, delay, stoppingToken);
        }
    }

    private async Task RequeueLaterAsync(QueuedEmail email, TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            queue.TryRequeue(email);
        }
        catch (OperationCanceledException)
        {
            // Host dayanır: proses daxili növbə itir; davamlı axınlar öz statusları ilə yenidən göndərir.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email göndərildi: {EmailKind} (cəhd {Attempt})")]
    private static partial void LogSent(ILogger logger, string emailKind, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email göndərilmədi: {EmailKind} (cəhd {Attempt}), {Delay} sonra yenidən; {ErrorType}")]
    private static partial void LogRetrying(ILogger logger, string emailKind, int attempt, TimeSpan delay, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email imtina olundu: {EmailKind}, {Attempt} cəhd uğursuz")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, string emailKind, int attempt);
}

/// <summary>
/// OTP kodunu şablonlu email ilə növbəyə qoyur. Development-də kod əlavə olaraq loga yazılır (yerli giriş üçün);
/// digər mühitlərdə kod <b>heç vaxt</b> loglanmır.
/// </summary>
public sealed partial class EmailOtpSender(
    IEmailTemplateRenderer templates, IEmailQueue queue, IHostEnvironment environment, ILogger<EmailOtpSender> logger) : IOtpSender
{
    public const string TemplateName = "otp";

    public async Task SendAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            LogDevelopmentCode(logger, email, code, expiresAt);
        }

        var model = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["validMinutes"] = ((int)Domain.Users.OtpPolicy.Validity.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["expiresAtBaku"] = BakuTime.ToBakuTime(expiresAt).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        };
        await queue.EnqueueAsync(templates.Render(TemplateName, model).To(email), TemplateName, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DEV giriş kodu {DevLoginCode} ({Email}, bitir {ExpiresAt:O})")]
    private static partial void LogDevelopmentCode(ILogger logger, string email, string devLoginCode, DateTimeOffset expiresAt);
}

public static class EmailRegistration
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddSingleton<ChannelEmailQueue>();
        services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<ChannelEmailQueue>());
        services.AddHostedService<EmailDispatcher>();
        return services;
    }
}
