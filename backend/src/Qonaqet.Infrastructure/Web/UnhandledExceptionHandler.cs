using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// İşlənməmiş exception → ProblemDetails, sabit <c>code</c> ilə. Production-da yalnız ümumi mesaj və
/// <c>traceId</c> qaytarılır — stack trace, exception mesajı və tipi heç vaxt sızmır. Logda exception
/// mesajı da yazılmır (şəxsi məlumat daşıya bilər), yalnız tip və trace id.
/// </summary>
public sealed partial class UnhandledExceptionHandler(IWebHostEnvironment environment, ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    public const string InternalErrorCode = "internal_error";
    public const string BadRequestCode = "bad_request";
    public const string CancelledCode = "request_cancelled";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (status, code, title) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, BadRequestCode, "Sorğu düzgün deyil."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, CancelledCode, "Sorğu ləğv edildi."),
            _ => (StatusCodes.Status500InternalServerError, InternalErrorCode, "Gözlənilməz xəta baş verdi."),
        };

        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        if (status >= 500)
        {
            LogUnhandled(logger, exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty, traceId);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://qonaqet.az/problems/{code}",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;

        if (environment.IsDevelopment())
        {
            problem.Detail = $"{exception.GetType().FullName}: {exception.Message}";
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "Unhandled {ExceptionType} for {Method} {Path} (trace {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType, string method, string path, string traceId);
}
