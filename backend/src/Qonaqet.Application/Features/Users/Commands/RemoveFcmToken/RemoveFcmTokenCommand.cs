using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Users.Commands.RemoveFcmToken;

/// <summary><c>DELETE me/fcm-token</c>: çıxışda cihaz token-ini silir (idempotent).</summary>
public sealed record RemoveFcmTokenCommand(string Token) : ICommand;

public sealed class RemoveFcmTokenValidator : Validator<RemoveFcmTokenCommand>
{
    public RemoveFcmTokenValidator()
    {
        RuleFor<string?>(x => x.Token).NotEmpty().MaxLength(4096);
    }
}

public sealed class RemoveFcmTokenHandler(IUserRepository users, ICurrentUser currentUser) : ICommandHandler<RemoveFcmTokenCommand>
{
    public async Task<Result> Handle(RemoveFcmTokenCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await users.RemoveFcmTokensAsync(UserId.From(currentUser.ActorId!.Value), [request.Token.Trim()], cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
