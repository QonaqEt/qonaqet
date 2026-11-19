using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Push;

/// <summary>Firebase konfiqurasiya olunmayanda (dev, testlər): push göndərilmir, yalnız cihaz sayı loglanır.</summary>
public sealed partial class NoopPushSender(ILogger<NoopPushSender> logger) : IPushSender
{
    public Task<PushSendResult> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        LogSkipped(logger, tokens.Count);
        return Task.FromResult(new PushSendResult([]));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Firebase konfiqurasiya olunmayıb; push {DeviceCount} cihaza göndərilmədi")]
    private static partial void LogSkipped(ILogger logger, int deviceCount);
}
