using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Commands.ClaimFromPool;

/// <summary>
/// <c>POST pool-claims</c> ("Hovuzdan al"): istifadəçi <c>acceptsPool</c> biznesin kataloq məhsulunu hovuz hesabına rezerv edir (30 dəqiqə).
/// Hovuzlar yalnız istifadəçinin <c>poolAccess</c> olan kateqoriyalarındandır: əvvəl ən tez bitən aktiv kampaniya hovuzu, sonra default hovuz.
/// Məbləğ hovuzda <b>tək şərtli atomik</b> yeniləmə ilə rezerv edilir (<c>balance − reserved ≥ qiymət</c>), ona görə paralel claim-lər balansı
/// heç vaxt mənfiyə salmır; heç bir hovuzda vəsait yoxdursa <c>pool_insufficient</c>. Rezerv yazıla bilməsə məbləğ dərhal geri buraxılır.
/// Tranzaksiyasızdır (E06-dakı təklif claim-i ilə eyni səbəb: isti hovuz sənədi tranzaksiyada WriteConflict verərdi); <c>PoolClaimReserved</c>
/// hadisəsi uğurdan sonra outbox-a düşür (ledger-də <c>PoolReserve</c>).
/// </summary>
[RequiresActiveAccount]
public sealed record ClaimFromPoolCommand(BusinessId BusinessId, CatalogItemId CatalogItemId) : ICommand<ClaimDto>, INonTransactionalCommand;

public sealed class ClaimFromPoolHandler(
    IBusinessRepository businesses,
    ICatalogItemRepository catalog,
    IPoolRepository pools,
    IClaimRepository claims,
    IUserRepository users,
    SocialEligibility eligibility,
    SocialLimits limits,
    ClaimRelease release,
    ClaimPresenter presenter,
    PoolAttribution attribution,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<ClaimFromPoolCommand, ClaimDto>
{
    public async Task<Result<ClaimDto>> Handle(ClaimFromPoolCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
        }

        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (business is not { IsActive: true })
        {
            return SocialErrors.OfferUnavailable;
        }

        if (!business.AcceptsPool)
        {
            return SocialErrors.PoolNotAccepted;
        }

        var item = await catalog.GetAsync(request.CatalogItemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.BusinessId != request.BusinessId || !item.IsActive)
        {
            return SocialErrors.CatalogItemInvalid;
        }

        var eligible = await eligibility.EligibleCategoriesAsync(user, now, cancellationToken).ConfigureAwait(false);
        var poolCategories = eligible.Where(c => c.Limits.PoolAccess).ToList();
        if (poolCategories.Count == 0)
        {
            return SocialErrors.CategoryNotEligible;
        }

        if (await claims.FindReservedForUserAsync(userId, cancellationToken).ConfigureAwait(false) is { } current)
        {
            if (current.IsRedeemableAt(now) || !await release.ExpireAsync(current, cancellationToken).ConfigureAwait(false))
            {
                return SocialErrors.ActiveClaimExists;
            }
        }

        var limit = await limits.CheckPoolAsync(userId, item.PriceQepik, eligible, poolCategories, now, cancellationToken).ConfigureAwait(false);
        if (limit.IsFailure)
        {
            return limit.Error;
        }

        var candidates = await pools.ListClaimablePoolsAsync([.. poolCategories.Select(c => c.Id)], now, cancellationToken).ConfigureAwait(false);
        foreach (var pool in candidates)
        {
            if (!await pools.TryReserveAsync(pool.Id, item.PriceQepik, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var donor = await attribution.ForReservationAsync(pool, cancellationToken).ConfigureAwait(false);
            var claim = Claim.ReserveFromPool(
                pool, business.Id, item.Id, item.Title, item.PriceQepik, userId, donor.Text, donor.DonationId, donor.UserId, donor.CorporateId, now);
            presenter.AttachCodes(claim);
            bool added;
            try
            {
                added = await claims.TryAddReservedAsync(claim, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pools.ReleaseReservationAsync(pool.Id, item.PriceQepik, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (!added)
            {
                await pools.ReleaseReservationAsync(pool.Id, item.PriceQepik, CancellationToken.None).ConfigureAwait(false);
                return SocialErrors.ActiveClaimExists;
            }

            return presenter.Present(claim, business.Name);
        }

        return SocialErrors.PoolInsufficient;
    }
}
