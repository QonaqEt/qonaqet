using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>Seçilmiş məhsul: kataloq elementi və say. Qiymət klientdən gəlmir.</summary>
public sealed record GiftItemRequest(Guid CatalogItemId, int Qty);

/// <summary>
/// Klientin "nə hədiyyə edirəm" seçimi. <paramref name="AmountQepik"/> yalnız vauçerdə (vauçerin nominalı) işlənir; məhsul hədiyyəsində
/// göndərilsə də nəzərə alınmır — məbləğ kataloqdan hesablanır.
/// </summary>
public sealed record GiftSelection(GiftKind Kind, GiftScope Scope, Guid? BusinessId, IReadOnlyList<GiftItemRequest>? Items, long? AmountQepik);

/// <summary>
/// Hədiyyənin məzmununu və qiymətini <b>yalnız serverdə</b> hesablayır:
/// <list type="bullet">
/// <item><c>Items</c>: bir aktiv biznesin 1–10 vahid <c>isGiftable</c>, aktiv məhsulu; qiymət kataloqdan (snapshot);</item>
/// <item>biznes vauçeri: biznesin <c>voucherMinQepik</c>–<c>voucherMaxQepik</c> aralığında;</item>
/// <item>şəbəkə vauçeri: 2–200 AZN (yalnız <c>Voucher</c>).</item>
/// </list>
/// </summary>
public sealed class GiftPricing(IBusinessRepository businesses, ICatalogItemRepository catalog)
{
    public async Task<Result<GiftContent>> PriceAsync(GiftSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(selection.Kind) || !Enum.IsDefined(selection.Scope))
        {
            return GiftErrors.ScopeInvalid;
        }

        if (selection.Scope == GiftScope.Network)
        {
            if (selection.Kind != GiftKind.Voucher || selection.BusinessId is not null)
            {
                return GiftErrors.ScopeInvalid;
            }

            return selection.AmountQepik is { } networkAmount && GiftRules.IsValidNetworkAmount(networkAmount)
                ? GiftContent.ForNetworkVoucher(networkAmount)
                : GiftErrors.AmountOutOfRange;
        }

        if (selection.BusinessId is not { } rawBusinessId)
        {
            return GiftErrors.BusinessUnavailable;
        }

        var businessId = BusinessId.From(rawBusinessId);
        var business = await businesses.GetAsync(businessId, cancellationToken).ConfigureAwait(false);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        if (!business.IsActive)
        {
            return GiftErrors.BusinessUnavailable;
        }

        if (selection.Kind == GiftKind.Voucher)
        {
            return selection.AmountQepik is { } amount && amount >= business.VoucherMinQepik && amount <= business.VoucherMaxQepik
                ? GiftContent.ForBusinessVoucher(businessId, amount)
                : GiftErrors.AmountOutOfRange;
        }

        var requested = selection.Items ?? [];
        if (requested.Count is 0 or > GiftRules.MaxLines || requested.Any(i => i.Qty <= 0)
            || requested.Sum(i => (long)i.Qty) > GiftRules.MaxItemUnits || requested.Select(i => i.CatalogItemId).Distinct().Count() != requested.Count)
        {
            return GiftErrors.ItemsInvalid;
        }

        var items = new List<GiftItem>(requested.Count);
        foreach (var line in requested)
        {
            var item = await catalog.GetAsync(CatalogItemId.From(line.CatalogItemId), cancellationToken).ConfigureAwait(false);
            if (item is null || item.BusinessId != businessId || !item.IsActive || !item.IsGiftable)
            {
                return GiftErrors.ItemUnavailable;
            }

            items.Add(GiftItem.Create(item.Id, item.Title, item.PriceQepik, line.Qty));
        }

        return GiftContent.ForItems(businessId, items);
    }
}
