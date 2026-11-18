using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// <c>X-Correlation-Id</c>: gələn başlıq (yalnız təhlükəsiz simvollar, ≤ 64) və ya trace ID. Cavaba yazılır,
/// log scope-una (<c>CorrelationId</c>) və cari <see cref="Activity"/>-yə (<c>correlation.id</c>) əlavə olunur,
/// beləliklə loglar və trace-lər eyni sorğuya bağlanır.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(incoming)
            ? incoming
            : Activity.Current?.TraceId.ToHexString() ?? Guid.CreateVersion7().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 64 && Safe().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Safe();
}
