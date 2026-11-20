using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Gifts.Commands.CreateGift;

/// <summary>
/// <c>POST gifts</c>: şəxsi hədiyyə. Məzmun və qiymət serverdə hesablanır (<see cref="GiftPricing"/>), hədiyyə <c>AwaitingPayment</c>
/// statusunda yaranır və ödəniş başladılır (<c>Idempotency-Key</c> məcburidir). Ödəniş təsdiqlənəndə consumer hədiyyəni <c>Scheduled</c> edir
/// və link token-i yaradır; göndərən linki <c>POST gifts/{id}/share-link</c> ilə bir dəfə götürür.
/// </summary>
/// <param name="RecipientEmail"><c>null</c> = "Linki özüm göndərəcəm".</param>
/// <param name="DeliverAt"><c>null</c> = dərhal; ən çox 90 gün irəli.</param>
[RequiresActiveAccount]
public sealed record CreateGiftCommand(
    GiftSelection What,
    string RecipientName,
    string? RecipientEmail,
    GiftTheme Theme,
    string? Message,
    SenderDisplay SenderDisplay,
    DateTimeOffset? DeliverAt,
    GiftExpiryAction ExpiryAction) : ICommand<GiftStartedDto>, INonTransactionalCommand, IIdempotentCommand;

/// <summary>Ödəniş başladı: tətbiq <see cref="PaymentUrl"/>-i açır və <c>GET payments/{id}</c>-ni sorğulayır.</summary>
public sealed record GiftStartedDto(Guid GiftId, Guid PaymentId, Uri PaymentUrl, long AmountQepik, DateTimeOffset DeliverAt, DateTimeOffset ExpiresAt);

public sealed class CreateGiftValidator : Validator<CreateGiftCommand>
{
    public CreateGiftValidator()
    {
        Rule("what", x => x.What is not null, GiftErrors.ItemsInvalid.Code, GiftErrors.ItemsInvalid.Message);
        Rule("recipientName", x => !string.IsNullOrWhiteSpace(x.RecipientName), GiftErrors.RecipientRequired.Code, GiftErrors.RecipientRequired.Message);
        RuleFor<string?>(x => x.RecipientName).MaxLength(GiftRules.RecipientNameMaxLength);
        RuleFor<string?>(x => x.RecipientEmail).Email().MaxLength(254);
        RuleFor<string?>(x => x.Message).MaxLength(GiftRules.MessageMaxLength);

        // Korporativ tema yalnız şirkət sifarişlərindədir (E10).
        Rule("theme", x => Enum.IsDefined(x.Theme) && x.Theme != GiftTheme.Corporate, "invalid_theme", "Tema düzgün deyil.");
        Rule("senderDisplay", x => Enum.IsDefined(x.SenderDisplay), "invalid_sender_display", "Göstərim düzgün deyil.");
        Rule("expiryAction", x => Enum.IsDefined(x.ExpiryAction), "invalid_expiry_action", "Müddət bitməsi seçimi düzgün deyil.");
    }
}

public sealed class CreateGiftHandler(
    IUserRepository users,
    GiftPricing pricing,
    GiftExpiryPools expiryPools,
    IGiftRepository gifts,
    IRedemptionCodes codes,
    PaymentStarter payments,
    IOptions<GiftOptions> options,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<CreateGiftCommand, GiftStartedDto>
{
    public async Task<Result<GiftStartedDto>> Handle(CreateGiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
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

        var expiry = await expiryPools.PlanAsync(request.ExpiryAction, cancellationToken).ConfigureAwait(false);
        if (expiry.IsFailure)
        {
            return expiry.Error;
        }

        var gift = Gift.Create(
            content.Value,
            new GiftSender(userId, null, request.SenderDisplay, SenderName(user)),
            new GiftRecipient(request.RecipientName, request.RecipientEmail),
            request.Theme,
            request.Message,
            deliverAt,
            options.Value.ValidityDays,
            expiry.Value,
            now);
        gift.AttachShortCode(GiftCodes.ShortCodeHash(codes, gift.Id));

        // Provayderə gedən təsvirdə ad, email, mesaj yoxdur.
        var started = await payments.StartAsync(
            new PaymentStart(PaymentPurpose.Gift, gift.Id.Value, gift.AmountQepik, "QONAQET · Hədiyyə", new PaymentPayer(userId)), cancellationToken)
            .ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        gift.AttachPayment(started.Value.PaymentId.Value);
        await gifts.AddAsync(gift, cancellationToken).ConfigureAwait(false);
        return new GiftStartedDto(gift.Id.Value, started.Value.PaymentId.Value, started.Value.PaymentUrl, gift.AmountQepik, gift.DeliverAt, gift.ExpiresAt);
    }

    /// <summary>Göndərənin snapshot adı: ad + soyadın baş hərfi; profil adı yoxdursa neytral ad.</summary>
    private static string SenderName(User user)
    {
        var first = user.FirstName?.Trim();
        if (string.IsNullOrEmpty(first))
        {
            return "QONAQET dostu";
        }

        var last = user.LastName?.Trim();
        var name = string.IsNullOrEmpty(last) ? first : $"{first} {last[0]}.";
        return name.Length > GiftRules.SenderNameMaxLength ? name[..GiftRules.SenderNameMaxLength] : name;
    }
}

/// <summary>Müddət bitməsi planı: bağışlamada konfiqurasiyadakı kateqoriyanın default hovuzu.</summary>
public sealed class GiftExpiryPools(ICategoryRepository categories, IPoolRepository pools, IOptions<GiftOptions> options)
{
    public async Task<Result<GiftExpiryPlan>> PlanAsync(GiftExpiryAction action, CancellationToken cancellationToken)
    {
        if (action == GiftExpiryAction.RefundToPayer)
        {
            return new GiftExpiryPlan(action, null);
        }

        var code = options.Value.ExpiryDonationCategoryCode;
        var category = string.IsNullOrWhiteSpace(code) ? null : await categories.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        Pool? pool = category is null ? null : await pools.GetDefaultForCategoryAsync(category.Id, cancellationToken).ConfigureAwait(false);
        return pool is null ? GiftErrors.ExpiryPoolUnavailable : new GiftExpiryPlan(action, pool.Id);
    }
}
