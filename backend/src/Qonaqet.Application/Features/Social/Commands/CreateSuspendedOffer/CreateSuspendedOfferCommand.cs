using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.CreateSuspendedOffer;

/// <summary>
/// <c>POST business/{businessId}/offers/suspended</c>: işçi kassada "askıda" qeyd edir — müştəri N məhsulun pulunu ödəyib
/// (pul kassada alınıb, platformadan keçmir, komissiya yoxdur). Ödəyən QONAQET istifadəçisidirsə <see cref="DonorEmail"/> ilə
/// bağlanır və o, yalnız aqreqatları görür (neçəsi verildi), alıcını yox.
/// </summary>
[RequiresActiveAccount]
public sealed record CreateSuspendedOfferCommand(
    BusinessId BusinessId,
    CategoryId CategoryId,
    string Title,
    CatalogItemId? CatalogItemId,
    int Quantity,
    DonorDisplay DonorDisplay,
    string? DonorName,
    string? DonorEmail) : ICommand<OfferDto>;

public sealed class CreateSuspendedOfferValidator : Validator<CreateSuspendedOfferCommand>
{
    public CreateSuspendedOfferValidator()
    {
        Rule("title", x => OfferRules.IsValidTitle(x.Title), "title_invalid", $"Başlıq boş ola bilməz, ən çox {Offer.TitleMaxLength} simvol.");
        Rule("quantity", x => Offer.IsValidQuantity(x.Quantity), "quantity_invalid", $"Say 1 ilə {Offer.MaxSuspendedQuantity} arasında olmalıdır.");
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "donor_display_invalid", "Göstərilmə növü Anonymous və ya Named olmalıdır.");
        Rule("donorName", x => OfferRules.IsValidDonorName(x.DonorName), ValidationCodes.TooLong, $"Ad ən çox {Offer.DonorNameMaxLength} simvol ola bilər.");
        RuleFor(x => x.DonorEmail).Email().When(x => !string.IsNullOrWhiteSpace(x.DonorEmail));
        Rule("catalogItemId", x => x.CatalogItemId is not { } id || id.Value != Guid.Empty, "offer_catalog_item_invalid", "Kataloq elementi düzgün deyil.");
    }
}

public sealed class CreateSuspendedOfferHandler(
    BusinessAccess access, IOfferRepository offers, IUserRepository users, OfferReferenceCheck references, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateSuspendedOfferCommand, OfferDto>
{
    public async Task<Result<OfferDto>> Handle(CreateSuspendedOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        if (!grant.Value.Business.IsActive)
        {
            return BusinessErrors.NotActive;
        }

        var category = await references.CategoryAsync(request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (category.IsFailure)
        {
            return category.Error;
        }

        if (!await references.CatalogItemUsableAsync(request.BusinessId, request.CatalogItemId, cancellationToken).ConfigureAwait(false))
        {
            return SocialErrors.CatalogItemInvalid;
        }

        Guid? donorUserId = null;
        if (!string.IsNullOrWhiteSpace(request.DonorEmail))
        {
            var donor = await users.GetByEmailAsync(request.DonorEmail.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
            if (donor is null)
            {
                return SocialErrors.DonorNotFound;
            }

            donorUserId = donor.Id.Value;
        }

        var staffId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var details = new SuspendedOfferDetails(request.Title, request.CatalogItemId, request.Quantity, request.DonorDisplay, request.DonorName, donorUserId);
        var offer = Offer.CreateSuspended(request.BusinessId, request.CategoryId, details, staffId, clock.UtcNow);
        await offers.AddAsync(offer, cancellationToken).ConfigureAwait(false);
        return offer.ToDto(category.Value, clock.BakuToday());
    }
}
