using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// Partner API-də hər POST üçün <c>Idempotency-Key</c> (OAuth token endpoint-i istisna — RFC 6749 axını özü təkrar oluna biləndir):
/// <list type="bullet">
/// <item>başlıq yoxdursa və ya çox uzundursa <c>400 idempotency_key_required</c>;</item>
/// <item>eyni client + açar + eyni marşrut və body → saxlanmış uğurlu cavab (status + body) təkrar qaytarılır, əməliyyat yenidən işləmir
/// (<c>Idempotent-Replayed: true</c>);</item>
/// <item>eyni açar fərqli body ilə → <c>409 idempotency_key_reused</c>.</item>
/// </list>
/// Yalnız uğurlu (2xx) cavablar saxlanılır ki, düzəldilmiş sorğu eyni açarla təkrar göndərilə bilsin. Qeydlər <c>idempotency_records</c>-da
/// tarixçə kimi qalır; Valkey yalnız keşdir.
/// </summary>
public sealed class PartnerIdempotencyFilter(IIdempotencyStore store, IClock clock) : IAsyncResourceFilter
{
    public const int MaxKeyLength = 200;

    public const string ReplayedHeader = "Idempotent-Replayed";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (!HttpMethods.IsPost(http.Request.Method) || http.Request.Path.StartsWithSegments("/oauth", StringComparison.OrdinalIgnoreCase))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var key = http.Request.Headers[HttpRequestContext.IdempotencyKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            context.Result = Result.Failure(CommonErrors.IdempotencyKeyRequired).ToActionResult();
            return;
        }

        var scope = $"partner:{http.User.FindFirst(QonaqetClaims.Subject)?.Value ?? "anonymous"}";
        var requestHash = await HashAsync(http.Request, http.RequestAborted).ConfigureAwait(false);
        var existing = await store.FindAsync(scope, key, http.RequestAborted).ConfigureAwait(false);
        if (existing is not null)
        {
            context.Result = Replay(existing, requestHash, http.Response);
            return;
        }

        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        finally
        {
            http.Response.Body = original;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, http.RequestAborted).ConfigureAwait(false);
        if (http.Response.StatusCode is >= 200 and < 300)
        {
            var stored = new JsonObject { ["status"] = http.Response.StatusCode, ["body"] = ParseBody(buffer.ToArray()) };
            await store.AddAsync(new IdempotencyRecord(scope, key, requestHash, stored.ToJsonString(), clock.UtcNow), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static IActionResult Replay(IdempotencyRecord record, string requestHash, HttpResponse response)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(record.RequestHash), Encoding.ASCII.GetBytes(requestHash)))
        {
            return Result.Failure(CommonErrors.IdempotencyKeyReused).ToActionResult();
        }

        var stored = JsonNode.Parse(record.ResponseJson)!.AsObject();
        response.Headers[ReplayedHeader] = "true";
        var body = stored["body"];
        return new ContentResult
        {
            StatusCode = stored["status"]!.GetValue<int>(),
            Content = body?.ToJsonString(),
            ContentType = body is null ? null : "application/json; charset=utf-8",
        };
    }

    /// <summary>Marşrut + body-nin SHA-256-sı: eyni açar başqa endpoint-də də "fərqli sorğu" sayılır.</summary>
    private static async Task<string> HashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;
        var prefix = Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n");
        return Convert.ToHexString(SHA256.HashData([.. prefix, .. body.ToArray()]));
    }

    private static JsonNode? ParseBody(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(Encoding.UTF8.GetString(body));
        }
    }
}
