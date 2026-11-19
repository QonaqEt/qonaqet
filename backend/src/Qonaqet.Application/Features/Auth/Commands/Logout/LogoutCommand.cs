using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Auth.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Refresh token-i ləğv edir. Access token-in vaxtı keçmiş olsa da çıxış mümkün olsun deyə ictimaidir; naməlum və ya
/// artıq ləğv olunmuş token üçün də uğur qaytarır (idempotent, heç nə sızdırmır).
/// </summary>
[PublicRequest]
public sealed record LogoutCommand(string RefreshToken) : ICommand;

public sealed class LogoutValidator : Validator<LogoutCommand>
{
    public LogoutValidator()
    {
        RuleFor<string?>(x => x.RefreshToken).NotEmpty().MaxLength(128);
    }
}

public sealed class LogoutHandler(IRefreshTokenRepository refreshTokens, SessionIssuer sessions, IClock clock) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await refreshTokens.FindByHashAsync(sessions.Hash(request.RefreshToken), cancellationToken).ConfigureAwait(false);
        if (token is { IsRevoked: false })
        {
            await refreshTokens.TryRevokeAsync(token.Id, clock.UtcNow, RefreshTokenRevokeReason.Logout, null, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
