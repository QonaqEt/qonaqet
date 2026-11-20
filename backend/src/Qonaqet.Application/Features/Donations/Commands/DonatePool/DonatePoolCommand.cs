using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Donations.Common;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Donations.Commands.DonatePool;

/// <summary>
/// <c>POST donations/pool</c>: kateqoriyanın default hovuzuna ianə (1–1000 AZN, adlı/anonim). <c>Pending</c> ianə yaradılır və ödəniş
/// başladılır; təsdiqdən sonra consumer hovuz balansını artırır və ledger-ə <c>PoolDeposit</c> yazır. Ianə geri qaytarılmır (cavabda yazılır).
/// </summary>
[RequiresActiveAccount]
public sealed record DonatePoolCommand(CategoryId CategoryId, long AmountQepik, DonorDisplay DonorDisplay, string? DonorName)
    : ICommand<DonationStartedDto>, INonTransactionalCommand, IIdempotentCommand;

public sealed class DonatePoolValidator : Validator<DonatePoolCommand>
{
    public DonatePoolValidator()
    {
        Rule("amountQepik", x => x.AmountQepik is >= DonationRules.MinAmountQepik and <= DonationRules.MaxAmountQepik,
            DonationErrors.AmountOutOfRange.Code, DonationErrors.AmountOutOfRange.Message);
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "invalid_donor_display", "Göstərim düzgün deyil.");
        RuleFor<string?>(x => x.DonorName).MaxLength(DonationRules.DonorNameMaxLength);
    }
}

public sealed class DonatePoolHandler(
    IPoolRepository pools, IUserRepository users, OfferReferenceCheck references, PoolDonationStarter starter, ICurrentUser currentUser)
    : ICommandHandler<DonatePoolCommand, DonationStartedDto>
{
    public async Task<Result<DonationStartedDto>> Handle(DonatePoolCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
        }

        var category = await references.CategoryAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var pool = await pools.GetDefaultForCategoryAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (pool is null)
        {
            return DonationErrors.PoolNotFound;
        }

        return await starter.StartAsync(pool.Id, campaignId: null, request.AmountQepik, request.DonorDisplay, request.DonorName, user, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Hovuz/kampaniya ianəsinin ortaq başlanğıcı: <c>Pending</c> ianə + ödəniş (məqsəd <c>Pool</c> və ya <c>Campaign</c>).</summary>
public sealed class PoolDonationStarter(IPoolDonationRepository donations, PaymentStarter payments, IClock clock)
{
    public async Task<Result<DonationStartedDto>> StartAsync(
        PoolId poolId, Guid? campaignId, long amountQepik, DonorDisplay display, string? requestedName, User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var name = DonationRules.DisplayName(display, requestedName, user);
        if (name.IsFailure)
        {
            return name.Error;
        }

        var donation = PoolDonation.Pledge(poolId, campaignId, amountQepik, display, name.Value, user.Id.Value, corporateId: null, clock.UtcNow);
        var purpose = campaignId is null ? PaymentPurpose.Pool : PaymentPurpose.Campaign;
        var started = await payments.StartAsync(
            new PaymentStart(purpose, donation.Id.Value, amountQepik, campaignId is null ? "QONAQET · Hovuz ianəsi" : "QONAQET · Kampaniya ianəsi", new PaymentPayer(user.Id.Value)),
            cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        donation.AttachPayment(started.Value.PaymentId.Value);
        await donations.AddAsync(donation, cancellationToken).ConfigureAwait(false);
        return new DonationStartedDto(donation.Id.Value, started.Value.PaymentId.Value, started.Value.PaymentUrl, amountQepik, Refundable: false,
            DonationRules.NonRefundableNotice);
    }
}
