using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Commands.CreateDailyOffer;

/// <summary>
/// <c>POST business/{businessId}/offers</c>: sahib gündəlik pulsuz təklif yaradır (məs. "tələbələrə gündə 20 çay").
/// Kvota hər Bakı günü yenilənir. Komissiya yoxdur.
/// </summary>
[RequiresActiveAccount]
public sealed record CreateDailyOfferCommand(
    BusinessId BusinessId,
    CategoryId CategoryId,
    string Title,
    CatalogItemId? CatalogItemId,
    int DailyQuota,
    string? AvailableFrom,
    string? AvailableTo,
    DonorDisplay DonorDisplay,
    string? DonorName,
    bool IsActive = true) : ICommand<OfferDto>, IDailyOfferInput;

public sealed class CreateDailyOfferValidator : DailyOfferValidator<CreateDailyOfferCommand>;

public sealed class CreateDailyOfferHandler(
    BusinessAccess access, IOfferRepository offers, OfferReferenceCheck references, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateDailyOfferCommand, OfferDto>
{
    public async Task<Result<OfferDto>> Handle(CreateDailyOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
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

        var offer = Offer.CreateDaily(request.BusinessId, request.CategoryId, request.ToDetails(), clock.UtcNow, currentUser.ActorId);
        await offers.AddAsync(offer, cancellationToken).ConfigureAwait(false);
        return offer.ToDto(category.Value, clock.BakuToday());
    }
}
