using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// Ən xarici behavior. Yalnız sorğunun <b>tip adını</b>, müddəti və nəticə kodunu loglayır —
/// sorğunun və cavabın məzmunu heç vaxt loglanmır, ona görə OTP, token, FIN və kart məlumatı
/// loga düşə bilməz.
/// </summary>
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (response is Result { IsFailure: true } failure)
            {
                LogFailed(logger, RequestName, failure.Error.Code, elapsed);
            }
            else
            {
                LogHandled(logger, RequestName, elapsed);
            }

            return response;
        }
        catch (Exception ex)
        {
            // Exception mesajı istifadəçi məlumatı daşıya bilər; yalnız tip adı yazılır.
            LogThrew(logger, RequestName, ex.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Request} handled in {ElapsedMs:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string request, double elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "{Request} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string request, string errorCode, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "{Request} threw {ExceptionType} after {ElapsedMs:0.0} ms")]
    private static partial void LogThrew(ILogger logger, string request, string exceptionType, double elapsedMs);
}
