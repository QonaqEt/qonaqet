using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Integration.Common;

/// <summary>
/// Partner sorğusunun client-i DB-dən oxunur: token 1 saat etibarlıdır, amma ləğv olunmuş client dərhal kəsilir. Bizneslərə giriş
/// (<c>business_ids</c>) və sandbox rejimi buradan yoxlanılır.
/// </summary>
public sealed class PartnerAccess(IIntegrationClientRepository clients, ICurrentUser currentUser)
{
    public async Task<Result<IntegrationClient>> RequireAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ActorId is not { } id || !currentUser.IsInRole(PartnerScopes.ClientRole))
        {
            return IntegrationErrors.ClientRevoked;
        }

        var client = await clients.GetAsync(IntegrationClientId.From(id), cancellationToken).ConfigureAwait(false);
        return client is null ? IntegrationErrors.ClientRevoked : client;
    }
}

public static class IntegrationSecrets
{
    /// <summary><see cref="ISecretHasher"/> məqsədi: client secret-lərinin hash-i.</summary>
    public const string Purpose = "integration-client-secret";

    public const int SecretBytes = 32;

    public const int ClientIdBytes = 12;

    public static string NewClientId(ISecureRandom random, bool sandbox)
    {
        ArgumentNullException.ThrowIfNull(random);
        return (sandbox ? "qc_test_" : "qc_live_") + random.Token(ClientIdBytes);
    }

    public static string NewSecret(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return "qs_" + random.Token(SecretBytes);
    }
}

/// <summary>Client-in görünüşü (secret heç vaxt; yalnız yaradılanda və rotasiyada <see cref="IntegrationClientSecretDto"/>-da bir dəfə).</summary>
public sealed record IntegrationClientDto(
    Guid Id,
    string Name,
    string ClientId,
    IntegrationOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyList<Guid> BusinessIds,
    IReadOnlyList<string> Scopes,
    bool IsSandbox,
    int RateLimitPerMinute,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? PreviousSecretExpiresAt,
    DateTimeOffset CreatedAt);

/// <param name="ClientSecret">Yalnız bu cavabda göstərilir — saxlanılmır və bir daha alına bilməz.</param>
public sealed record IntegrationClientSecretDto(IntegrationClientDto Client, string ClientSecret);

public static class IntegrationMapping
{
    public static IntegrationClientDto ToDto(this IntegrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new IntegrationClientDto(
            client.Id.Value, client.Name, client.ClientId, client.OwnerType, client.OwnerId, client.BusinessIds, client.Scopes, client.IsSandbox,
            client.RateLimitPerMinute, client.LastUsedAt, client.PreviousSecretExpiresAt, client.CreatedAt);
    }
}
