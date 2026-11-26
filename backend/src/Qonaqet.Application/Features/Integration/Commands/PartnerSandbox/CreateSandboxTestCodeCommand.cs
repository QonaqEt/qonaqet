using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerSandbox;

/// <summary>
/// <c>POST /partner/v1/sandbox/test-codes</c> (yalnız sandbox client, <c>codes:read</c>): client-in sandbox biznesi üçün təhvilə hazır test
/// vauçeri yaradır və kodlarını qaytarır (E13-T09). Pul hərəkəti yoxdur: hədiyyə balansdan ödənilmir, sandbox olduğundan ledger-ə yazılmır və
/// yalnız sandbox biznesdə təhvil olunur. <c>Idempotency-Key</c> məcburidir.
/// </summary>
[RequiresRole(PartnerScopes.CodesRead)]
public sealed record CreateSandboxTestCodeCommand(Guid BusinessId, long? AmountQepik) : ICommand<SandboxTestCodeDto>;

/// <param name="ShortCode">Kassada əl ilə yazılan kod (<c>XXXX-XXXX</c>).</param>
/// <param name="QrPayload">QR-ın mətni (skanerin oxuduğu dəyər).</param>
public sealed record SandboxTestCodeDto(Guid GiftId, Guid BusinessId, long AmountQepik, string ShortCode, string QrPayload, DateTimeOffset ExpiresAt);

public static class SandboxTestCodes
{
    public const long DefaultAmountQepik = 50_00;

    public const long MaxAmountQepik = 200_00;
}

public sealed class CreateSandboxTestCodeValidator : Validator<CreateSandboxTestCodeCommand>
{
    public CreateSandboxTestCodeValidator()
    {
        Rule(
            "amountQepik",
            x => x.AmountQepik is null or (> 0 and <= SandboxTestCodes.MaxAmountQepik),
            "invalid_amount",
            "Test vauçerinin məbləği 0.01 – 200 AZN olmalıdır.");
    }
}

public sealed class CreateSandboxTestCodeHandler(
    PartnerAccess access,
    IGiftRepository gifts,
    IRedemptionCodes codes,
    IQrCodeEncoder qr,
    IGiftLinkTokens links,
    IOptions<GiftOptions> options,
    IClock clock) : ICommandHandler<CreateSandboxTestCodeCommand, SandboxTestCodeDto>
{
    public async Task<Result<SandboxTestCodeDto>> Handle(CreateSandboxTestCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        if (!client.Value.IsSandbox)
        {
            return IntegrationErrors.SandboxOnly;
        }

        var target = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (partner, business) = target.Value;
        var now = clock.UtcNow;
        var amount = request.AmountQepik ?? SandboxTestCodes.DefaultAmountQepik;
        var gift = Gift.CreateSandboxTestVoucher(business.Id, amount, partner.Id.Value, partner.Name, options.Value.ValidityDays, now);
        gift.AttachShortCode(GiftCodes.ShortCodeHash(codes, gift.Id));
        gift.ConfirmPaid(links.Issue().Secret, now);
        gift.Deliver(null, now);
        await gifts.AddAsync(gift, cancellationToken).ConfigureAwait(false);

        var presented = GiftCodes.Present(codes, qr, gift.Id);
        return new SandboxTestCodeDto(gift.Id.Value, business.Id.Value, amount, presented.ShortCode, presented.QrPayload, gift.ExpiresAt);
    }
}
