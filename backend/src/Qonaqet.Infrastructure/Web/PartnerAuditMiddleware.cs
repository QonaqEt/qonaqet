using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// Partner API-nin hər autentifikasiya olunmuş çağırışı <c>audit_logs</c>-a client id ilə yazılır: metod, marşrut <b>şablonu</b> (məs.
/// <c>partner/v1/codes/{code}</c> — kodun özü yox, o bearer dəyərdir), status və korrelyasiya id-si. Body, token və şəxsi məlumat yazılmır.
/// Audit yazısı uğursuz olsa cavab dəyişmir (loglanır).
/// </summary>
public sealed partial class PartnerAuditMiddleware(RequestDelegate next, ILogger<PartnerAuditMiddleware> logger)
{
    public const string Action = "PartnerCall";

    public const string ActorType = "IntegrationClient";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            if (Guid.TryParse(context.User.FindFirst(QonaqetClaims.Subject)?.Value, out var clientId))
            {
                await WriteAsync(context, clientId).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteAsync(HttpContext context, Guid clientId)
    {
        try
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";

            // Ayrıca kontekst: sorğunun öz kontekstində yarımçıq qalmış dəyişikliklər audit ilə birlikdə yazılmasın.
            await using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            db.Add(new AuditLogRecord
            {
                Id = Guid.CreateVersion7(),
                ActorId = clientId,
                ActorType = ActorType,
                Action = Action,
                EntityType = "PartnerApi",
                Data = JsonSerializer.Serialize(
                    new { method = context.Request.Method, route, status = context.Response.StatusCode, correlationId = context.TraceIdentifier },
                    OutboxSerializer.JsonOptions),
                CreatedAt = context.RequestServices.GetRequiredService<IClock>().UtcNow,
            });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Audit xətası partnyorun cavabını pozmamalıdır.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAuditFailed(ex, clientId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Partner çağırışının auditi yazılmadı: client {ClientId}")]
    private partial void LogAuditFailed(Exception exception, Guid clientId);
}
