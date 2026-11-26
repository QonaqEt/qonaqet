using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.OwnerIntegrationClients;

/// <summary>
/// <c>POST business/integrations/clients</c> (E13-T11): biznes sahibi öz bizneslərinə (bir və ya şəbəkə) bağlı kassa/POS client-i yaradır.
/// Hər <c>businessId</c>-də sahib olmalıdır; scope-lar yalnız biznes scope-larıdır (<see cref="PartnerScopes.Business"/>). Secret cavabda bir
/// dəfə. Dəyişikliklər Postgres audit interceptor-u ilə <c>audit_logs</c>-a düşür.
/// </summary>
[RequiresActiveAccount]
public sealed record CreateOwnerIntegrationClientCommand(string Name, IReadOnlyList<Guid> BusinessIds, IReadOnlyList<string> Scopes, bool IsSandbox)
    : ICommand<IntegrationClientSecretDto>, IPostgresCommand;

/// <summary><c>POST business/integrations/clients/{id}/rotate-secret</c>: yeni secret; köhnəsi 24 saat da işləyir.</summary>
[RequiresActiveAccount]
public sealed record RotateOwnerIntegrationClientSecretCommand(IntegrationClientId ClientId) : ICommand<IntegrationClientSecretDto>, IPostgresCommand;

/// <summary><c>DELETE business/integrations/clients/{id}</c>: ləğv (soft delete), token-lər dərhal işləməz olur.</summary>
[RequiresActiveAccount]
public sealed record RevokeOwnerIntegrationClientCommand(IntegrationClientId ClientId) : ICommand, IPostgresCommand;

/// <summary><c>GET business/integrations/clients</c>: sahibin bizneslərinə bağlı client-lər (secret-siz).</summary>
[RequiresActiveAccount]
public sealed record ListOwnerIntegrationClientsQuery : IQuery<IReadOnlyList<IntegrationClientDto>>;

public static class OwnerIntegrationClients
{
    public const int RateLimitPerMinute = 120;

    public const string RevokeReason = "revoked_by_owner";

    /// <summary>Client sahibə məxsusdur: <c>Business</c> tiplidir və bütün biznesləri üzərində sahibin <c>Manage</c> icazəsi var.</summary>
    public static async Task<Result<IntegrationClient>> RequireOwnedAsync(
        IIntegrationClientRepository clients, BusinessAccess access, IntegrationClientId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(access);
        var client = await clients.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (client is null || client.OwnerType != IntegrationOwnerType.Business || client.BusinessIds.Count == 0)
        {
            return IntegrationErrors.ClientNotFound;
        }

        foreach (var businessId in client.BusinessIds)
        {
            if ((await access.RequireAsync(BusinessId.From(businessId), BusinessPermission.Manage, cancellationToken).ConfigureAwait(false)).IsFailure)
            {
                return IntegrationErrors.ClientNotFound; // başqasının client-i: varlığı açıqlanmır
            }
        }

        return client;
    }
}

public sealed class CreateOwnerIntegrationClientValidator : Validator<CreateOwnerIntegrationClientCommand>
{
    public CreateOwnerIntegrationClientValidator()
    {
        Rule(
            "client",
            x => x.BusinessIds is { Count: > 0 } && IntegrationClient.IsValid(x.Name, x.BusinessIds, x.Scopes, OwnerIntegrationClients.RateLimitPerMinute),
            IntegrationErrors.ClientInvalid.Code,
            IntegrationErrors.ClientInvalid.Message);
        Rule(
            "scopes",
            x => x.Scopes is not null && x.Scopes.All(s => PartnerScopes.Business.Contains(s, StringComparer.Ordinal)),
            "scope_not_allowed",
            "Biznes client-i yalnız codes:read, redemptions:write, catalog:write və webhooks scope-larını ala bilər.");
    }
}

public sealed class CreateOwnerIntegrationClientHandler(
    IIntegrationClientRepository clients, BusinessAccess access, ISecretHasher hasher, ISecureRandom random, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateOwnerIntegrationClientCommand, IntegrationClientSecretDto>
{
    public async Task<Result<IntegrationClientSecretDto>> Handle(CreateOwnerIntegrationClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var businessIds = request.BusinessIds.Distinct().ToList();
        foreach (var businessId in businessIds)
        {
            var grant = await access.RequireAsync(BusinessId.From(businessId), BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
            if (grant.IsFailure)
            {
                return grant.Error;
            }

            if (grant.Value.Business.IsSandbox != request.IsSandbox)
            {
                return IntegrationErrors.SandboxMismatch;
            }
        }

        var secret = IntegrationSecrets.NewSecret(random);
        var client = IntegrationClient.Create(
            request.Name, IntegrationSecrets.NewClientId(random, request.IsSandbox), hasher.Hash(IntegrationSecrets.Purpose, secret),
            IntegrationOwnerType.Business, businessIds[0], businessIds, request.Scopes, request.IsSandbox, OwnerIntegrationClients.RateLimitPerMinute,
            clock.UtcNow, currentUser.ActorId);
        clients.Add(client);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrationClientSecretDto(client.ToDto(), secret);
    }
}

public sealed class RotateOwnerIntegrationClientSecretHandler(
    IIntegrationClientRepository clients, BusinessAccess access, ISecretHasher hasher, ISecureRandom random, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RotateOwnerIntegrationClientSecretCommand, IntegrationClientSecretDto>
{
    public async Task<Result<IntegrationClientSecretDto>> Handle(RotateOwnerIntegrationClientSecretCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await OwnerIntegrationClients.RequireOwnedAsync(clients, access, request.ClientId, cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var secret = IntegrationSecrets.NewSecret(random);
        client.Value.RotateSecret(hasher.Hash(IntegrationSecrets.Purpose, secret), clock.UtcNow, currentUser.ActorId);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrationClientSecretDto(client.Value.ToDto(), secret);
    }
}

public sealed class RevokeOwnerIntegrationClientHandler(
    IIntegrationClientRepository clients, BusinessAccess access, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RevokeOwnerIntegrationClientCommand>
{
    public async Task<Result> Handle(RevokeOwnerIntegrationClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await OwnerIntegrationClients.RequireOwnedAsync(clients, access, request.ClientId, cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        client.Value.SoftDelete(clock.UtcNow, currentUser.ActorId, OwnerIntegrationClients.RevokeReason);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed class ListOwnerIntegrationClientsHandler(
    IIntegrationClientRepository clients, IBusinessMembershipRepository memberships, ICurrentUser currentUser)
    : IQueryHandler<ListOwnerIntegrationClientsQuery, IReadOnlyList<IntegrationClientDto>>
{
    public async Task<Result<IReadOnlyList<IntegrationClientDto>>> Handle(ListOwnerIntegrationClientsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.ActorId is not { } actor)
        {
            return CommonErrors.Unauthorized;
        }

        var owned = (await memberships.ListForUserAsync(actor, cancellationToken).ConfigureAwait(false))
            .Where(m => m.Role == MembershipRole.Owner).Select(m => m.BusinessId.Value).ToHashSet();
        var candidates = await clients.ListForBusinessesAsync(owned, cancellationToken).ConfigureAwait(false);

        // Yalnız bütün biznesləri sahibə məxsus olanlar (başqası ilə ortaq şəbəkə client-i göstərilmir).
        return candidates.Where(c => c.BusinessIds.All(owned.Contains)).Select(c => c.ToDto()).ToList();
    }
}
