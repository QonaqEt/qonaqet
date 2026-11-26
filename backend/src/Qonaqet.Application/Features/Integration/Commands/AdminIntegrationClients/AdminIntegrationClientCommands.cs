using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.AdminIntegrationClients;

/// <summary>
/// <c>POST admin/integration-clients</c>: admin partnyor üçün client yaradır. Cavabda <c>client_secret</c> bir dəfə göstərilir; saxlanılan yalnız
/// HMAC hash-idir. Yaradılma audit log-a düşür (Postgres audit interceptor-u).
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateIntegrationClientCommand(
    string Name,
    IntegrationOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyList<Guid> BusinessIds,
    IReadOnlyList<string> Scopes,
    bool IsSandbox,
    int RateLimitPerMinute) : ICommand<IntegrationClientSecretDto>, IPostgresCommand;

/// <summary><c>POST admin/integration-clients/{id}/rotate-secret</c>: yeni secret (bir dəfə göstərilir); köhnəsi 24 saat da etibarlıdır.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RotateIntegrationClientSecretCommand(IntegrationClientId ClientId) : ICommand<IntegrationClientSecretDto>, IPostgresCommand;

/// <summary><c>DELETE admin/integration-clients/{id}</c>: ləğv (soft delete) — token-ləri dərhal işləməz olur.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RevokeIntegrationClientCommand(IntegrationClientId ClientId) : ICommand, IPostgresCommand;

/// <summary><c>GET admin/integration-clients?ownerType=&amp;ownerId=</c>.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListIntegrationClientsQuery(IntegrationOwnerType? OwnerType, Guid? OwnerId) : IQuery<IReadOnlyList<IntegrationClientDto>>;

public sealed class CreateIntegrationClientValidator : Validator<CreateIntegrationClientCommand>
{
    public CreateIntegrationClientValidator()
    {
        Rule(
            "client",
            x => IntegrationClient.IsValid(x.Name, x.BusinessIds, x.Scopes, x.RateLimitPerMinute) && x.OwnerId != Guid.Empty,
            IntegrationErrors.ClientInvalid.Code,
            IntegrationErrors.ClientInvalid.Message);
    }
}

public sealed class CreateIntegrationClientHandler(
    IIntegrationClientRepository clients, IBusinessRepository businesses, ISecretHasher hasher, ISecureRandom random, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateIntegrationClientCommand, IntegrationClientSecretDto>
{
    public async Task<Result<IntegrationClientSecretDto>> Handle(CreateIntegrationClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Sandbox client yalnız sandbox bizneslərə, real client yalnız real bizneslərə bağlanır (E13-T09).
        foreach (var businessId in request.BusinessIds.Distinct())
        {
            var business = await businesses.GetAsync(BusinessId.From(businessId), cancellationToken).ConfigureAwait(false);
            if (business is not null && business.IsSandbox != request.IsSandbox)
            {
                return IntegrationErrors.SandboxMismatch;
            }
        }

        var secret = IntegrationSecrets.NewSecret(random);
        var client = IntegrationClient.Create(
            request.Name, IntegrationSecrets.NewClientId(random, request.IsSandbox), hasher.Hash(IntegrationSecrets.Purpose, secret), request.OwnerType,
            request.OwnerId, request.BusinessIds, request.Scopes, request.IsSandbox, request.RateLimitPerMinute, clock.UtcNow, currentUser.ActorId);
        clients.Add(client);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrationClientSecretDto(client.ToDto(), secret);
    }
}

public sealed class RotateIntegrationClientSecretHandler(
    IIntegrationClientRepository clients, ISecretHasher hasher, ISecureRandom random, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RotateIntegrationClientSecretCommand, IntegrationClientSecretDto>
{
    public async Task<Result<IntegrationClientSecretDto>> Handle(RotateIntegrationClientSecretCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await clients.GetAsync(request.ClientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return IntegrationErrors.ClientNotFound;
        }

        var secret = IntegrationSecrets.NewSecret(random);
        client.RotateSecret(hasher.Hash(IntegrationSecrets.Purpose, secret), clock.UtcNow, currentUser.ActorId);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrationClientSecretDto(client.ToDto(), secret);
    }
}

public sealed class RevokeIntegrationClientHandler(IIntegrationClientRepository clients, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RevokeIntegrationClientCommand>
{
    public const string Reason = "revoked";

    public async Task<Result> Handle(RevokeIntegrationClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await clients.GetAsync(request.ClientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return IntegrationErrors.ClientNotFound;
        }

        client.SoftDelete(clock.UtcNow, currentUser.ActorId, Reason);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed class ListIntegrationClientsHandler(IIntegrationClientRepository clients) : IQueryHandler<ListIntegrationClientsQuery, IReadOnlyList<IntegrationClientDto>>
{
    public async Task<Result<IReadOnlyList<IntegrationClientDto>>> Handle(ListIntegrationClientsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await clients.ListAsync(request.OwnerType, request.OwnerId, 500, cancellationToken).ConfigureAwait(false)).Select(c => c.ToDto()).ToList();
    }
}
