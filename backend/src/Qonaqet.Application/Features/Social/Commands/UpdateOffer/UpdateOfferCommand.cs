using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.UpdateOffer;

/// <summary>
/// <c>PUT business/{businessId}/offers/{offerId}</c>: sahib təklifi dəyişir. Gündəlik təklifdə kvota, pəncərə və aktivlik;
/// askıda təklifində yalnız başlıq, göstərilmə və aktivlik (say dəyişmir). Kateqoriya dəyişmir — yeni təklif yaradılır.
/// </summary>
[RequiresActiveAccount]
public sealed record UpdateOfferCommand(
    BusinessId BusinessId,
    OfferId OfferId,
    string Title,
    CatalogItemId? CatalogItemId,
    int DailyQuota,
    string? AvailableFrom,
    string? AvailableTo,
    DonorDisplay DonorDisplay,
    string? DonorName,
    bool IsActive) : ICommand<OfferDto>, IDailyOfferInput;

public sealed class UpdateOfferValidator : Validator<UpdateOfferCommand>
{
    public UpdateOfferValidator()
    {
        Rule("title", x => OfferRules.IsValidTitle(x.Title), "title_invalid", $"Başlıq boş ola bilməz, ən çox {Offer.TitleMaxLength} simvol.");
        Rule("availableFrom", x => Offer.IsValidWindow(x.AvailableFrom, x.AvailableTo), "offer_window_invalid",
            "Vaxt pəncərəsi ya boş olmalıdır, ya da iki fərqli HH:mm vaxtı.");
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "donor_display_invalid", "Göstərilmə növü Anonymous və ya Named olmalıdır.");
        Rule("donorName", x => OfferRules.IsValidDonorName(x.DonorName), ValidationCodes.TooLong, $"Ad ən çox {Offer.DonorNameMaxLength} simvol ola bilər.");
    }
}

public sealed class UpdateOfferHandler(
    BusinessAccess access, IOfferRepository offers, ICategoryRepository categories, OfferReferenceCheck references, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateOfferCommand, OfferDto>
{
    public async Task<Result<OfferDto>> Handle(UpdateOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var offer = await offers.GetAsync(request.OfferId, cancellationToken).ConfigureAwait(false);
        if (offer is null || offer.BusinessId != request.BusinessId)
        {
            return SocialErrors.OfferNotFound;
        }

        var now = clock.UtcNow;
        switch (offer.Type)
        {
            case OfferType.BusinessDaily:
                if (!Offer.IsValidDailyQuota(request.DailyQuota))
                {
                    return Error.Validation("daily_quota_invalid", $"Gündəlik kvota 1 ilə {Offer.MaxDailyQuota} arasında olmalıdır.");
                }

                if (!await references.CatalogItemUsableAsync(request.BusinessId, request.CatalogItemId, cancellationToken).ConfigureAwait(false))
                {
                    return SocialErrors.CatalogItemInvalid;
                }

                offer.UpdateDaily(request.ToDetails(), now, currentUser.ActorId);
                break;
            case OfferType.Suspended:
                offer.UpdateSuspended(request.Title, request.DonorDisplay, request.DonorName, request.IsActive, now, currentUser.ActorId);
                break;
            default:
                // Onlayn alınmış (Remote) təkliflər ianəçinin ödənişidir; biznes onları dəyişmir (E08).
                return SocialErrors.OfferNotEditable;
        }

        await offers.UpdateAsync(offer, cancellationToken).ConfigureAwait(false);
        var category = await categories.GetAsync(offer.CategoryId, cancellationToken).ConfigureAwait(false);
        return offer.ToDto(category, clock.BakuToday());
    }
}
