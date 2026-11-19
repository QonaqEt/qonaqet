using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Commands.ClaimOffer;

/// <summary>
/// <c>POST offers/{id}/claim</c>: uyğun istifadəçi təklifdən bir vahid rezerv edir (30 dəqiqə). Vahid tək şərtli atomik yeniləmə ilə
/// götürülür (<see cref="IOfferRepository.TryTakeUnitAsync"/>), ona görə paralel claim-lər kvotanı heç vaxt aşmır; bitəndə
/// <c>quota_exhausted</c>. Götürmə tranzaksiyadan kənardadır, ona görə rezerv yazıla bilməsə vahid dərhal geri qaytarılır.
/// </summary>
[RequiresActiveAccount]
public sealed record ClaimOfferCommand(OfferId OfferId) : ICommand<ClaimDto>;

public sealed class ClaimOfferHandler(
    IOfferRepository offers,
    IClaimRepository claims,
    IBusinessRepository businesses,
    ICategoryRepository categories,
    IUserRepository users,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<ClaimOfferCommand, ClaimDto>
{
    public async Task<Result<ClaimDto>> Handle(ClaimOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
        }

        var offer = await offers.GetAsync(request.OfferId, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return SocialErrors.OfferNotFound;
        }

        var business = await businesses.GetAsync(offer.BusinessId, cancellationToken).ConfigureAwait(false);
        if (!offer.IsActive || business is not { IsActive: true })
        {
            return SocialErrors.OfferUnavailable;
        }

        var category = await categories.GetAsync(offer.CategoryId, cancellationToken).ConfigureAwait(false);
        if (category is null || !SocialEligibility.IsEligible(user, category, now))
        {
            return SocialErrors.CategoryNotEligible;
        }

        if (offer.Type == OfferType.BusinessDaily && !offer.IsWithinWindow(now))
        {
            return SocialErrors.OutsideWindow;
        }

        if (await claims.FindReservedForUserAsync(userId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return SocialErrors.ActiveClaimExists;
        }

        var today = Offer.DateKey(BakuTime.ToBakuDate(now));
        if (!await offers.TryTakeUnitAsync(offer, today, cancellationToken).ConfigureAwait(false))
        {
            return SocialErrors.QuotaExhausted;
        }

        var claim = Claim.ReserveFromOffer(offer, userId, DonorTexts.Claim(offer, business), today, now);
        bool added;
        try
        {
            added = await claims.TryAddReservedAsync(claim, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await offers.ReleaseUnitAsync(offer.Id, offer.Type, claim.QuotaDate, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (!added)
        {
            // Eyni istifadəçinin paralel claim-i qalib gəldi: götürülmüş vahid geri qaytarılır.
            await offers.ReleaseUnitAsync(offer.Id, offer.Type, claim.QuotaDate, CancellationToken.None).ConfigureAwait(false);
            return SocialErrors.ActiveClaimExists;
        }

        return claim.ToDto(business.Name);
    }
}
