using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using CorporateAggregate = Qonaqet.Domain.Corporate.Corporate;

namespace Qonaqet.Application.Features.Corporate.Common;

public sealed record CorporateDto(Guid Id, string Name, string Voen, Guid? LogoFileId, string? BrandColor, string ContactEmail);

/// <summary>Korporativ sifarişin görünüşü. Alıcı siyahısı burada yoxdur (yalnız say) — sifariş siyahıları yüngül qalsın.</summary>
public sealed record CorporateOrderDto(
    Guid Id,
    GiftKind Kind,
    GiftScope Scope,
    Guid? BusinessId,
    IReadOnlyList<GiftItemDto> Items,
    long UnitPriceQepik,
    string? Message,
    DateTimeOffset DeliverAt,
    GiftExpiryAction ExpiryAction,
    int RecipientCount,
    long TotalQepik,
    CorporatePaymentMethod? PaymentMethod,
    Guid? PaymentId,
    string? BankReference,
    CorporateOrderStatus Status,
    int SentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CompletedAt);

public static class CorporateMapping
{
    public static CorporateDto ToDto(this CorporateAggregate corporate)
    {
        ArgumentNullException.ThrowIfNull(corporate);
        return new CorporateDto(corporate.Id.Value, corporate.Name, corporate.Voen, corporate.LogoFileId, corporate.BrandColor, corporate.ContactEmail);
    }

    public static CorporateOrderDto ToDto(this CorporateGiftOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new CorporateOrderDto(
            order.Id.Value, order.Kind, order.Scope, order.BusinessId?.Value,
            [.. order.Items.Select(i => new GiftItemDto(i.CatalogItemId.Value, i.Title, i.UnitPriceQepik, i.Qty, i.Qty))],
            order.UnitPriceQepik, order.Message, order.DeliverAt, order.ExpiryAction, order.RecipientCount, order.TotalQepik, order.PaymentMethod,
            order.PaymentId, order.BankReference, order.Status, order.SentCount, order.CreatedAt, order.PaidAt, order.CompletedAt);
    }
}

/// <summary>
/// Korporativ portalın girişi: cari istifadəçinin şirkəti DB-dən oxunur (JWT-dəki rol kifayət deyil — admin istifadəçini başqa şirkətə keçirə
/// bilər). Başqa şirkətin sifarişi heç vaxt görünmür.
/// </summary>
public sealed class CorporateAccess(IUserRepository users, ICorporateRepository corporates, ICorporateGiftOrderRepository orders, ICurrentUser currentUser)
{
    public async Task<Result<CorporateAggregate>> RequireAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user?.CorporateId is not { } corporateId || !user.HasRole(Role.CorporateUser))
        {
            return CorporateErrors.NoCorporate;
        }

        var corporate = await corporates.GetAsync(CorporateId.From(corporateId), cancellationToken).ConfigureAwait(false);
        return corporate is null ? CorporateErrors.NoCorporate : corporate;
    }

    /// <summary>Şirkətin öz sifarişi; başqasınınkı <c>corporate_order_not_found</c>.</summary>
    public async Task<Result<(CorporateAggregate Corporate, CorporateGiftOrder Order)>> RequireOrderAsync(CorporateGiftOrderId orderId, CancellationToken cancellationToken)
    {
        var corporate = await RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        var order = await orders.GetAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.CorporateId != corporate.Value.Id)
        {
            return CorporateErrors.OrderNotFound;
        }

        return (corporate.Value, order);
    }
}
