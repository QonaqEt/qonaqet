using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Auth.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Auth.Commands.RefreshSession;

/// <summary>
/// Refresh token rotasiyası: köhnə token ləğv olunur, yeni cüt verilir. Ləğv olunmuş token yenidən gələrsə
/// (oğurlanmış token-in təkrar istifadəsi) istifadəçinin bütün sessiyaları bağlanır — bu yan təsir xəta
/// qaytarılsa da commit olunur (<see cref="ICommitsOnFailure"/>).
/// </summary>
[PublicRequest]
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<AuthTokensDto>, ICommitsOnFailure;

public sealed class RefreshSessionValidator : Validator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        RuleFor<string?>(x => x.RefreshToken).NotEmpty().MaxLength(128);
    }
}

public sealed class RefreshSessionHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    SessionIssuer sessions,
    IClock clock) : ICommandHandler<RefreshSessionCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var token = await refreshTokens.FindByHashAsync(sessions.Hash(request.RefreshToken), cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return AuthErrors.RefreshTokenInvalid;
        }

        if (token.IsRevoked)
        {
            await refreshTokens.RevokeAllForUserAsync(token.UserId, now, RefreshTokenRevokeReason.ReuseDetected, cancellationToken).ConfigureAwait(false);
            return AuthErrors.RefreshTokenReused;
        }

        if (token.IsExpired(now))
        {
            return AuthErrors.RefreshTokenInvalid;
        }

        var user = await users.GetAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            await refreshTokens.TryRevokeAsync(token.Id, now, RefreshTokenRevokeReason.AccountDeleted, null, cancellationToken).ConfigureAwait(false);
            return AuthErrors.RefreshTokenInvalid;
        }

        var (tokens, newId) = await sessions.IssueAsync(user, now, cancellationToken).ConfigureAwait(false);
        if (!await refreshTokens.TryRevokeAsync(token.Id, now, RefreshTokenRevokeReason.Rotated, newId, cancellationToken).ConfigureAwait(false))
        {
            // Paralel sorğu eyni token-i artıq rotasiya edib: təkrar istifadə kimi qəbul olunur.
            await refreshTokens.RevokeAllForUserAsync(token.UserId, now, RefreshTokenRevokeReason.ReuseDetected, cancellationToken).ConfigureAwait(false);
            return AuthErrors.RefreshTokenReused;
        }

        return tokens;
    }
}
