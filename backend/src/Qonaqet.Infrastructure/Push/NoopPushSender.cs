using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Push;

/// <summary>FCM göndəricisi (FirebaseAdmin) E03-T05-də gəlir; o vaxta qədər push göndərilmir, yalnız say loglanır.</summary>
public sealed partial class NoopPushSender(ILogger<NoopPushSender> logger) : IPushSender
{
    public Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        LogSkipped(logger, tokens.Count);
        return Task.FromResult(new PushSendResult([]));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Push göndəricisi konfiqurasiya olunmayıb; {DeviceCount} cihaza göndərilmədi (E03-T05)")]
    private static partial void LogSkipped(ILogger logger, int deviceCount);
}
