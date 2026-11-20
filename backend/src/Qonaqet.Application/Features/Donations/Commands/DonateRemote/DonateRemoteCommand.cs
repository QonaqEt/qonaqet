using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Donations.Common;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Donations.Commands.DonateRemote;

/// <summary>
/// <c>POST donations/remote</c>: ianəçi biznesin kataloq məhsulundan bir kateqoriya üçün onlayn alır ("uzaqdan ısmarla"). Məbləğ serverdə
/// kataloq qiymətindən hesablanır (<c>qiymət × say</c>); klientdən məbləğ qəbul edilmir. <c>Remote</c> təklif ödəniş təsdiqlənənə qədər
/// görünməz yaradılır və ödəniş başladılır; təsdiqdən sonra consumer onu aktivləşdirir. Ianəçi alıcını nə seçir, nə görür.
/// </summary>
[RequiresActiveAccount]
public sealed record DonateRemoteCommand(
    BusinessId BusinessId, CatalogItemId CatalogItemId, CategoryId CategoryId, int Quantity, DonorDisplay DonorDisplay, string? DonorName)
    : ICommand<DonationStartedDto>, INonTransactionalCommand, IIdempotentCommand;

public sealed class DonateRemoteValidator : Validator<DonateRemoteCommand>
{
    public DonateRemoteValidator()
    {
        Rule("quantity", x => Offer.IsValidQuantity(x.Quantity) && x.Quantity <= 100, "invalid_quantity", "Say 1–100 arasında olmalıdır.");
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "invalid_donor_display", "Göstərim düzgün deyil.");
        RuleFor<string?>(x => x.DonorName).MaxLength(DonationRules.DonorNameMaxLength);
    }
}

public sealed class DonateRemoteHandler(
    IBusinessRepository businesses,
    ICatalogItemRepository catalog,
    IOfferRepository offers,
    IUserRepository users,
    OfferReferenceCheck references,
    PaymentStarter payments,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<DonateRemoteCommand, DonationStartedDto>
{
    public async Task<Result<DonationStartedDto>> Handle(DonateRemoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
        }

        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        if (!business.IsActive)
        {
            return DonationErrors.BusinessUnavailable;
        }

        var category = await references.CategoryAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var item = await catalog.GetAsync(request.CatalogItemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.BusinessId != request.BusinessId || !item.IsActive)
        {
            return DonationErrors.ItemUnavailable;
        }

        var amount = item.PriceQepik * request.Quantity;
        if (amount > DonationRules.MaxRemoteTotalQepik)
        {
            return DonationErrors.AmountOutOfRange;
        }

        var donorName = DonationRules.DisplayName(request.DonorDisplay, request.DonorName, user);
        if (donorName.IsFailure)
        {
            return donorName.Error;
        }

        var offer = Offer.CreateRemote(
            request.BusinessId, request.CategoryId, item.Id, item.Title, item.PriceQepik, request.Quantity, request.DonorDisplay, donorName.Value.Length == 0 ? null : donorName.Value, userId, clock.UtcNow);
        var started = await payments.StartAsync(
            new PaymentStart(PaymentPurpose.Remote, offer.Id.Value, offer.TotalPriceQepik, "QONAQET · İsmarla", new PaymentPayer(userId)),
            cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        offer.AttachPayment(started.Value.PaymentId.Value);
        await offers.AddAsync(offer, cancellationToken).ConfigureAwait(false);
        return new DonationStartedDto(
            offer.Id.Value, started.Value.PaymentId.Value, started.Value.PaymentUrl, offer.TotalPriceQepik, Refundable: false, DonationRules.NonRefundableNotice);
    }
}
