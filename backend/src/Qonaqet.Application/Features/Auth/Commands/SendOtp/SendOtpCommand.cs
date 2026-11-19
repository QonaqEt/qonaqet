using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Auth.Commands.SendOtp;

/// <summary>
/// Email-ə 6 rəqəmli giriş kodu göndərir. Cavab email-in qeydiyyatda olub-olmamasından asılı deyil
/// (hesab mövcudluğu sezilmir); limit email üzrədir: 15 dəqiqədə ən çox 3 göndərmə.
/// </summary>
[PublicRequest]
public sealed record SendOtpCommand(string Email) : ICommand;

public sealed class SendOtpValidator : Validator<SendOtpCommand>
{
    public SendOtpValidator()
    {
        RuleFor<string?>(x => x.Email).NotEmpty().MaxLength(EmailAddress.MaxLength).Email();
    }
}

public sealed class SendOtpHandler(
    IOtpCodeRepository otpCodes,
    ISecretHasher hasher,
    ISecureRandom random,
    IOtpSender sender,
    IClock clock) : ICommandHandler<SendOtpCommand>
{
    public const string HashPurpose = "otp";

    public async Task<Result> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var email = EmailAddress.Normalize(request.Email);
        var now = clock.UtcNow;

        var recent = await otpCodes.CountIssuedSinceAsync(email, now - OtpPolicy.SendWindow, cancellationToken).ConfigureAwait(false);
        if (recent >= OtpPolicy.MaxSendsPerWindow)
        {
            return AuthErrors.OtpSendLimit;
        }

        var code = random.NumericCode(OtpPolicy.Digits);
        var otp = OtpCode.Issue(email, hasher.Hash(HashPurpose, code), now);
        await otpCodes.AddAsync(otp, cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(email, code, otp.ExpiresAt, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
