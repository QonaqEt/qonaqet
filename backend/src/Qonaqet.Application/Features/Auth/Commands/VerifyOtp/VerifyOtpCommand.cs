using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Auth.Commands.SendOtp;
using Qonaqet.Application.Features.Auth.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Auth.Commands.VerifyOtp;

/// <summary>
/// Kodu yoxlayır; email-in hesabı yoxdursa yaradır (qeydiyyat: email → kod → ad) və sessiya açır (access + refresh token). Hər yanlış cəhd sayılır,
/// 5 cəhddən sonra kod yararsızdır. Bütün uğursuzluqlar eyni <c>otp_invalid</c> kodunu qaytarır.
/// </summary>
[PublicRequest]
public sealed record VerifyOtpCommand(string Email, string Code) : ICommand<OtpVerifiedDto>;

/// <summary><see cref="ProfileCompleted"/> = <c>false</c> olanda klient ad addımını göstərir.</summary>
public sealed record OtpVerifiedDto(UserId UserId, bool IsNewUser, bool ProfileCompleted, AuthTokensDto Tokens);

public sealed class VerifyOtpValidator : Validator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor<string?>(x => x.Email).NotEmpty().MaxLength(EmailAddress.MaxLength).Email();
        RuleFor(x => x.Code).NotEmpty().Must(
            c => c is { Length: OtpPolicy.Digits } && c.All(char.IsAsciiDigit),
            "otp_format",
            "Kod 6 rəqəmdən ibarət olmalıdır.");
    }
}

public sealed class VerifyOtpHandler(
    IOtpCodeRepository otpCodes,
    IUserRepository users,
    ISecretHasher hasher,
    SessionIssuer sessions,
    IClock clock) : ICommandHandler<VerifyOtpCommand, OtpVerifiedDto>
{
    public async Task<Result<OtpVerifiedDto>> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var email = EmailAddress.Normalize(request.Email);
        var now = clock.UtcNow;

        var otp = await otpCodes.ConsumeAttemptAsync(email, now, cancellationToken).ConfigureAwait(false);
        if (otp is null
            || !hasher.Verify(SendOtpHandler.HashPurpose, request.Code, otp.CodeHash)
            || !await otpCodes.TryMarkUsedAsync(otp.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.OtpInvalid;
        }

        var user = await users.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        if (user is { IsBlocked: true })
        {
            return AuthErrors.UserBlocked;
        }

        var isNew = user is null;
        if (user is null)
        {
            user = User.Register(email, now);
            await users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        }

        var (tokens, _) = await sessions.IssueAsync(user, now, cancellationToken).ConfigureAwait(false);
        return new OtpVerifiedDto(user.Id, isNew, user.ProfileCompleted, tokens);
    }
}
