using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Push;

/// <summary>
/// <c>Firebase</c> bölməsi. Service account JSON yalnız mühitdən/sirrdən gəlir (<c>Firebase__CredentialsJson</c>);
/// verilməyibsə (dev) push göndərilmir — <see cref="NoopPushSender"/>.
/// </summary>
public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string? CredentialsJson { get; set; }

    public string? ProjectId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(CredentialsJson);
}

/// <summary>
/// FCM (FirebaseAdmin) göndəricisi: token-lər 500-lük paketlərlə göndərilir; provayderin artıq qəbul etmədiyi
/// token-lər (<c>Unregistered</c>, etibarsız arqument, sender uyğunsuzluğu) <see cref="PushSendResult.InvalidTokens"/>
/// kimi qaytarılır və <c>UserPushNotifier</c> onları dərhal təmizləyir. Token-lər loglanmır.
/// </summary>
public sealed partial class FcmPushSender(FirebaseMessaging messaging, ILogger<FcmPushSender> logger) : IPushSender
{
    /// <summary>FCM-in bir multicast sorğusunda qəbul etdiyi maksimum token sayı.</summary>
    public const int BatchSize = 500;

    public async Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(message);
        var invalid = new List<string>();
        foreach (var batch in tokens.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
        {
            var multicast = new MulticastMessage
            {
                Fids = batch,
                Notification = new Notification { Title = message.Title, Body = message.Body },
                Data = message.Data?.ToDictionary(StringComparer.Ordinal),
            };

            var response = await messaging.SendEachForMulticastAsync(multicast, cancellationToken).ConfigureAwait(false);
            var errors = response.Responses.Select(r => r.IsSuccess ? null : r.Exception?.MessagingErrorCode).ToArray();
            invalid.AddRange(InvalidTokens(batch, errors));
            if (response.FailureCount > 0)
            {
                LogFailures(logger, response.FailureCount, batch.Length);
            }
        }

        return new PushSendResult(invalid);
    }

    /// <summary>Cavab xətalarından həmişəlik etibarsız token-ləri seçir (müvəqqəti xətalar — kvota, server — sayılmır).</summary>
    public static IReadOnlyList<string> InvalidTokens(IReadOnlyList<string> tokens, IReadOnlyList<MessagingErrorCode?> errors)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(errors);
        return [.. tokens.Where((_, i) => i < errors.Count && errors[i] is MessagingErrorCode.Unregistered
            or MessagingErrorCode.InvalidArgument or MessagingErrorCode.SenderIdMismatch)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push: {FailureCount}/{DeviceCount} cihaza çatmadı")]
    private static partial void LogFailures(ILogger logger, int failureCount, int deviceCount);
}

public static class PushRegistration
{
    private const string AppName = "qonaqet";

    public static IServiceCollection AddPush(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(FirebaseOptions.SectionName);
        services.Configure<FirebaseOptions>(section);
        if (!section.Get<FirebaseOptions>()?.IsConfigured ?? true)
        {
            services.AddSingleton<IPushSender, NoopPushSender>();
            return services;
        }

        services.AddSingleton(sp => FirebaseMessaging.GetMessaging(GetOrCreateApp(sp.GetRequiredService<IOptions<FirebaseOptions>>().Value)));
        services.AddSingleton<IPushSender, FcmPushSender>();
        return services;
    }

    private static FirebaseApp GetOrCreateApp(FirebaseOptions options)
    {
        var existing = FirebaseApp.GetInstance(AppName);
        if (existing is not null)
        {
            return existing;
        }

        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(options.CredentialsJson!).ToGoogleCredential();
        return FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = options.ProjectId }, AppName);
    }
}
