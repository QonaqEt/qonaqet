using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Users.Commands.DeleteMe;

/// <summary>
/// Hesabın silinməsi = soft delete (audit log ilə) + bütün sessiyaların bağlanması; köhnə hesaba giriş artıq mümkün deyil.
/// Email partial unique index sayəsində yeni hesab üçün sərbəst qalır.
/// </summary>
public sealed record DeleteMeCommand(string? Reason = null) : ICommand;

public sealed class DeleteMeHandler(IUserRepository users, IRefreshTokenRepository refreshTokens, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<DeleteMeCommand>
{
    public const int ReasonMaxLength = 500;

    public async Task<Result> Handle(DeleteMeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = UserId.From(currentUser.ActorId!.Value);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Self-service" : request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, ReasonMaxLength)];

        if (!await users.SoftDeleteAsync(userId, userId.Value, reason, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.UserNotFound;
        }

        await refreshTokens.RevokeAllForUserAsync(userId, clock.UtcNow, RefreshTokenRevokeReason.AccountDeleted, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
