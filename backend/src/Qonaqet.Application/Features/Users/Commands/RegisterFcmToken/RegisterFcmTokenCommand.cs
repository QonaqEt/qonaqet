using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Users.Commands.RegisterFcmToken;

/// <summary><c>POST me/fcm-token</c>: cihaz token-ini əlavə edir; eyni token ikinci dəfə dublikat yaratmır.</summary>
public sealed record RegisterFcmTokenCommand(string Token) : ICommand;

public sealed class RegisterFcmTokenValidator : Validator<RegisterFcmTokenCommand>
{
    public const int TokenMaxLength = 4096;

    public RegisterFcmTokenValidator()
    {
        RuleFor<string?>(x => x.Token).NotEmpty().MaxLength(TokenMaxLength);
    }
}

public sealed class RegisterFcmTokenHandler(IUserRepository users, ICurrentUser currentUser, IClock clock) : ICommandHandler<RegisterFcmTokenCommand>
{
    public async Task<Result> Handle(RegisterFcmTokenCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        user.AddFcmToken(request.Token.Trim(), clock.UtcNow);
        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
