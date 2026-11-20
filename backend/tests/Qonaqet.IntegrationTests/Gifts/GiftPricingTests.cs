using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Hədiyyənin qiyməti yalnız serverdə (E09-T01): kataloq snapshot-ı, vauçer aralıqları, şəbəkə vauçeri.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class GiftPricingTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Items_are_priced_from_the_catalog_and_must_be_giftable()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gprice");

        var priced = await PriceAsync(host, new GiftSelection(GiftKind.Items, GiftScope.Business, shop.Id, [new(shop.CoffeeId, 3)], AmountQepik: 1));
        Assert.True(priced.IsSuccess);
        Assert.Equal(3 * 3_50, priced.Value.AmountQepik); // klientin göndərdiyi məbləğ nəzərə alınmır
        Assert.Equal("Kofe", Assert.Single(priced.Value.Items).Title);

        var notGiftable = await PriceAsync(host, new GiftSelection(GiftKind.Items, GiftScope.Business, shop.Id, [new(shop.HiddenId, 1)], null));
        Assert.Equal("gift_item_unavailable", notGiftable.Error.Code);

        var tooMany = await PriceAsync(host, new GiftSelection(GiftKind.Items, GiftScope.Business, shop.Id, [new(shop.CoffeeId, 11)], null));
        Assert.Equal("gift_items_invalid", tooMany.Error.Code);

        var foreign = await PriceAsync(host, new GiftSelection(GiftKind.Items, GiftScope.Business, Guid.CreateVersion7(), [new(shop.CoffeeId, 1)], null));
        Assert.Equal("business_not_found", foreign.Error.Code);
    }

    [Fact]
    public async Task Vouchers_respect_business_limits_and_the_network_range()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gvoucher");

        Assert.Equal(50_00, (await PriceAsync(host, new GiftSelection(GiftKind.Voucher, GiftScope.Business, shop.Id, null, 50_00))).Value.AmountQepik);
        Assert.Equal("gift_amount_out_of_range", (await PriceAsync(host, new GiftSelection(GiftKind.Voucher, GiftScope.Business, shop.Id, null, 1_00))).Error.Code);
        Assert.Equal("gift_amount_out_of_range", (await PriceAsync(host, new GiftSelection(GiftKind.Voucher, GiftScope.Business, shop.Id, null, null))).Error.Code);

        var network = await PriceAsync(host, new GiftSelection(GiftKind.Voucher, GiftScope.Network, null, null, 200_00));
        Assert.Equal((GiftScope.Network, (Domain.Businesses.BusinessId?)null), (network.Value.Scope, network.Value.BusinessId));
        Assert.Equal("gift_amount_out_of_range", (await PriceAsync(host, new GiftSelection(GiftKind.Voucher, GiftScope.Network, null, null, 200_01))).Error.Code);
        Assert.Equal("gift_scope_invalid", (await PriceAsync(host, new GiftSelection(GiftKind.Items, GiftScope.Network, null, [new(shop.CoffeeId, 1)], null))).Error.Code);
    }

    private static async Task<SharedKernel.Result<GiftContent>> PriceAsync(Identity.ApiTestHost host, GiftSelection selection)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GiftPricing>().PriceAsync(selection, Ct);
    }
}
