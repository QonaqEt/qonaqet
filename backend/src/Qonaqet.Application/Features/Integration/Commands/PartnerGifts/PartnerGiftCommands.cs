using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Gifts.Events;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerGifts;

/// <summary>
/// <c>POST /partner/v1/gifts</c> (<c>gifts:issue</c>): issuer (bank, çatdırılma tətbiqi, HR) öz istifadəçisinə QONAQET hədiyyəsi buraxır —
/// şəxsi hədiyyə modeli + <c>externalReference</c>. Məbləğ serverdə hesablanır və partnyorun ön ödənişli balansından silinir:
/// <list type="number">
/// <item>hədiyyə Mongo-da <c>AwaitingPayment</c> yaranır;</item>
/// <item>Postgres tranzaksiyasında balans kilidlənib yoxlanılır, <c>PartnerIssue</c> yazılır, <c>PartnerGiftFunded</c> outbox-a düşür
/// (balans çatmırsa hədiyyə bağlanır → <c>insufficient_partner_balance</c>);</item>
/// <item>sürətli yol: hədiyyə dərhal <c>Scheduled</c> olur və link bir dəfə cavabda qaytarılır. Bu addım yarıda qalsa, consumer hədiyyəni
/// hadisədən <c>Scheduled</c> edir (ödəniş təsdiqi ilə eyni nümunə).</item>
/// </list>
/// </summary>
[RequiresRole(PartnerScopes.GiftsIssue)]
public sealed record PartnerIssueGiftCommand(
    GiftSelection What,
    string RecipientName,
    string? RecipientEmail,
    GiftTheme Theme,
    string? Message,
    DateTimeOffset? DeliverAt,
    string? ExternalReference) : ICommand<PartnerGiftDto>, INonTransactionalCommand;

/// <summary><c>POST /partner/v1/gifts/{id}/cancel</c> (<c>gifts:issue</c>): hələ çatdırılmamış (<c>Scheduled</c>) hədiyyə; məbləğ balansa qayıdır.</summary>
[RequiresRole(PartnerScopes.GiftsIssue)]
public sealed record PartnerCancelGiftCommand(GiftId GiftId) : ICommand<PartnerGiftDto>, INonTransactionalCommand;

/// <param name="Link">Hədiyyə linki — yalnız buraxılış cavabında bir dəfə (bearer-dir, saxlanılmır).</param>
public sealed record PartnerGiftDto(
    Guid Id,
    string Status,
    GiftKind Kind,
    GiftScope Scope,
    long AmountQepik,
    long RemainingQepik,
    DateTimeOffset DeliverAt,
    DateTimeOffset ExpiresAt,
    string? ExternalReference,
    Uri? Link = null);

public static class PartnerGiftMapping
{
    public static PartnerGiftDto ToPartnerDto(this Gift gift, DateTimeOffset now, Uri? link = null)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return new PartnerGiftDto(
            gift.Id.Value, GiftPresenter.EffectiveStatus(gift, now).ToString(), gift.Kind, gift.Scope, gift.AmountQepik, gift.RemainingQepik, gift.DeliverAt,
            gift.ExpiresAt, gift.ExternalReference, link);
    }
}

public sealed class PartnerIssueGiftValidator : Validator<PartnerIssueGiftCommand>
{
    public PartnerIssueGiftValidator()
    {
        Rule("what", x => x.What is not null, GiftErrors.ItemsInvalid.Code, GiftErrors.ItemsInvalid.Message);
        Rule("recipientName", x => !string.IsNullOrWhiteSpace(x.RecipientName), GiftErrors.RecipientRequired.Code, GiftErrors.RecipientRequired.Message);
        RuleFor<string?>(x => x.RecipientName).MaxLength(GiftRules.RecipientNameMaxLength);
        RuleFor<string?>(x => x.RecipientEmail).Email().MaxLength(254);
        RuleFor<string?>(x => x.Message).MaxLength(GiftRules.MessageMaxLength);
        RuleFor<string?>(x => x.ExternalReference).MaxLength(Gift.ExternalReferenceMaxLength);
        Rule("theme", x => Enum.IsDefined(x.Theme) && x.Theme != GiftTheme.Corporate, "invalid_theme", "Tema düzgün deyil.");
    }
}

public sealed class PartnerIssueGiftHandler(
    PartnerAccess access,
    GiftPricing pricing,
    IGiftRepository gifts,
    IRedemptionCodes codes,
    IPartnerFunding funding,
    GiftFulfillment fulfillment,
    IGiftLinkTokens links,
    IAppUrls urls,
    IOptions<GiftOptions> options,
    IClock clock) : ICommandHandler<PartnerIssueGiftCommand, PartnerGiftDto>
{
    public async Task<Result<PartnerGiftDto>> Handle(PartnerIssueGiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var now = clock.UtcNow;
        var deliverAt = request.DeliverAt?.ToUniversalTime() ?? now;
        if (deliverAt > now + GiftRules.MaxScheduleAhead)
        {
            return GiftErrors.DeliverAtOutOfRange;
        }

        var content = await pricing.PriceAsync(request.What, cancellationToken).ConfigureAwait(false);
        if (content.IsFailure)
        {
            return content.Error;
        }

        var partner = client.Value;
        var gift = Gift.CreateForPartner(
            content.Value, partner.Id.Value, partner.Name, new GiftRecipient(request.RecipientName, request.RecipientEmail), request.Theme, request.Message,
            deliverAt, options.Value.ValidityDays, request.ExternalReference, now);
        gift.AttachShortCode(GiftCodes.ShortCodeHash(codes, gift.Id));
        await gifts.AddAsync(gift, cancellationToken).ConfigureAwait(false);

        if (!await funding.TryFundGiftAsync(partner.Id.Value, gift.Id.Value, gift.AmountQepik, now, cancellationToken).ConfigureAwait(false))
        {
            await fulfillment.OnUnpaidAsync(gift.Id, now, cancellationToken).ConfigureAwait(false);
            return IntegrationErrors.InsufficientPartnerBalance;
        }

        // Sürətli yol: Scheduled + link (consumer da eyni keçidi idempotent edir).
        await fulfillment.OnPaidAsync(gift.Id, now, cancellationToken).ConfigureAwait(false);
        var funded = await gifts.GetAsync(gift.Id, cancellationToken).ConfigureAwait(false) ?? gift;
        var version = funded.Version;
        var token = links.Unseal(funded.LinkTokenSealed);
        Uri? link = null;
        if (token is not null && funded.RevealLink(now) && await gifts.TryReplaceAsync(funded, version, cancellationToken).ConfigureAwait(false))
        {
            link = urls.GiftLink(token);
        }

        return funded.ToPartnerDto(now, link);
    }
}

public sealed class PartnerCancelGiftHandler(PartnerAccess access, IGiftRepository gifts, GiftTransactions transactions, IClock clock)
    : ICommandHandler<PartnerCancelGiftCommand, PartnerGiftDto>
{
    public async Task<Result<PartnerGiftDto>> Handle(PartnerCancelGiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        if (gift is null || gift.PartnerClientId != client.Value.Id.Value)
        {
            return GiftErrors.NotFound;
        }

        if (gift.Status != GiftStatus.Scheduled)
        {
            return GiftErrors.NotCancellable;
        }

        var now = clock.UtcNow;
        var version = gift.Version;
        gift.Cancel(now, client.Value.Id.Value);
        return await transactions.SaveAsync(gift, version, cancellationToken).ConfigureAwait(false)
            ? gift.ToPartnerDto(now)
            : GiftErrors.Changed;
    }
}
