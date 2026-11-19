using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.BlockUser;

/// <summary>
/// Admin istifadəçini bloklayır: bütün sessiyalar bağlanır, <see cref="UserBlocked"/> hadisəsi outbox-a yazılır
/// (aktiv claim-lər ləğv olunur — E06-T08; lövhələrdən gizlənir — E11), audit log-a düşür.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record BlockUserCommand(UserId UserId, string Reason) : ICommand;

public sealed class BlockUserValidator : Validator<BlockUserCommand>
{
    public BlockUserValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(500);
    }
}

public sealed class BlockUserHandler(
    IUserRepository users, IRefreshTokenRepository refreshTokens, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<BlockUserCommand>
{
    public async Task<Result> Handle(BlockUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserId.Value == currentUser.ActorId)
        {
            return AuthErrors.CannotBlockSelf;
        }

        var user = await users.GetAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        var now = clock.UtcNow;
        if (!user.Block(request.Reason, now, currentUser.ActorId))
        {
            return Result.Success();
        }

        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, RefreshTokenRevokeReason.AccountBlocked, cancellationToken).ConfigureAwait(false);
        var audit = new AuditRecorded(now, currentUser.ActorId, "User", UserAuditActions.Block, nameof(User), user.Id.Value,
            new Dictionary<string, string?> { ["reason"] = user.BlockReason });
        await outbox.AddAsync([audit], cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

[RequiresRole(RoleNames.Admin)]
public sealed record UnblockUserCommand(UserId UserId) : ICommand;

public sealed class UnblockUserHandler(IUserRepository users, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<UnblockUserCommand>
{
    public async Task<Result> Handle(UnblockUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        var now = clock.UtcNow;
        if (user.Unblock(now, currentUser.ActorId))
        {
            await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
            await outbox.AddAsync([new AuditRecorded(now, currentUser.ActorId, "User", UserAuditActions.Unblock, nameof(User), user.Id.Value)], cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }
}
