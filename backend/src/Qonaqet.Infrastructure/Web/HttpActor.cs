using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Web;

/// <summary>JWT claim adları (<c>MapInboundClaims = false</c>, ona görə qısa adlar olduğu kimi qalır).</summary>
public static class QonaqetClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string BusinessId = "business_id";
    public const string CorporateId = "corporate_id";
}

/// <summary>HTTP sorğusunun aktoru: autentifikasiya olunmuş principal-ın <c>sub</c> və <c>role</c> claim-ləri.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && ActorId is not null;

    public Guid? ActorId =>
        Guid.TryParse(Principal?.FindFirst(QonaqetClaims.Subject)?.Value, out var id) && id != Guid.Empty ? id : null;

    public IReadOnlySet<string> Roles =>
        Principal?.FindAll(QonaqetClaims.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal) ?? [];

    public Guid? BusinessId => GuidClaim(QonaqetClaims.BusinessId);

    public Guid? CorporateId => GuidClaim(QonaqetClaims.CorporateId);

    private Guid? GuidClaim(string type) =>
        Guid.TryParse(Principal?.FindFirst(type)?.Value, out var id) && id != Guid.Empty ? id : null;
}

public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public string? IdempotencyKey =>
        accessor.HttpContext?.Request.Headers[IdempotencyKeyHeader] is { Count: > 0 } values && !string.IsNullOrWhiteSpace(values[0])
            ? values[0]
            : null;
}

/// <summary>HTTP-dən kənar host-lar (Worker): autentifikasiya olunmuş aktor yoxdur.</summary>
public sealed class AnonymousActor : ICurrentUser, IRequestContext
{
    public bool IsAuthenticated => false;

    public Guid? ActorId => null;

    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();

    public Guid? BusinessId => null;

    public Guid? CorporateId => null;

    public string? IdempotencyKey => null;
}
