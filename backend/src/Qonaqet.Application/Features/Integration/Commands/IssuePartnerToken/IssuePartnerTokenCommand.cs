using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Commands.IssuePartnerToken;

/// <summary>
/// <c>POST /oauth/token</c> (OAuth2 client credentials, RFC 6749 §4.4): <c>client_id</c> + <c>client_secret</c> → 1 saatlıq JWT. Secret cari
/// və ya (rotasiyadan sonra 24 saat) əvvəlki secret-in hash-i ilə müqayisə olunur. <c>scope</c> boşluqla ayrılmış siyahıdır; boşdursa client-in
/// bütün scope-ları verilir, artıq olan scope <c>invalid_scope</c>. Naməlum client və yanlış secret eyni xətanı (<c>invalid_client</c>) alır.
/// </summary>
[PublicRequest]
public sealed record IssuePartnerTokenCommand(string? GrantType, string? ClientId, string? ClientSecret, string? Scope)
    : ICommand<PartnerTokenDto>, IPostgresCommand;

/// <summary>RFC 6749 §5.1 cavabı (<c>access_token</c>, <c>token_type</c>, <c>expires_in</c>, <c>scope</c>).</summary>
public sealed record PartnerTokenDto(string AccessToken, string TokenType, int ExpiresIn, string Scope);

public sealed class IssuePartnerTokenHandler(
    IIntegrationClientRepository clients, ISecretHasher hasher, IPartnerTokenIssuer issuer, IClock clock)
    : ICommandHandler<IssuePartnerTokenCommand, PartnerTokenDto>
{
    public const string ClientCredentials = "client_credentials";

    public async Task<Result<PartnerTokenDto>> Handle(IssuePartnerTokenCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.GrantType, ClientCredentials, StringComparison.Ordinal))
        {
            return IntegrationErrors.UnsupportedGrantType;
        }

        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrEmpty(request.ClientSecret))
        {
            return IntegrationErrors.InvalidClient;
        }

        var now = clock.UtcNow;
        var client = await clients.FindByClientIdAsync(request.ClientId.Trim(), cancellationToken).ConfigureAwait(false);
        if (client is null
            || !(hasher.Verify(IntegrationSecrets.Purpose, request.ClientSecret, client.SecretHash)
                || (client.AcceptsPreviousSecretAt(now) && hasher.Verify(IntegrationSecrets.Purpose, request.ClientSecret, client.PreviousSecretHash!))))
        {
            return IntegrationErrors.InvalidClient;
        }

        var requested = (request.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scopes = client.GrantScopes(requested);
        if (scopes is null)
        {
            return IntegrationErrors.InvalidScope;
        }

        client.MarkUsed(now);
        await clients.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var token = issuer.Issue(client, scopes, now);
        return new PartnerTokenDto(token.Token, "Bearer", (int)Math.Round((token.ExpiresAt - now).TotalSeconds), string.Join(' ', scopes));
    }
}
