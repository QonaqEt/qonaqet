using System.Net;
using System.Reflection;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Gifts.Commands.CancelGift;
using Qonaqet.Application.Features.Gifts.Commands.CreateGift;
using Qonaqet.Application.Features.Gifts.Commands.ReissueGiftLink;
using Qonaqet.Application.Features.Gifts.Commands.RevealGiftLink;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Bloklanmış istifadəçi hədiyyə yarada bilmir (E09-T10).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class BlockedGiftSenderTests(PaymentsFixture fixture)
{
    [Fact]
    public void Gift_commands_that_start_or_move_payments_require_an_active_account()
    {
        foreach (var command in new[] { typeof(CreateGiftCommand), typeof(CancelGiftCommand), typeof(RevealGiftLinkCommand), typeof(ReissueGiftLinkCommand) })
        {
            Assert.NotNull(command.GetCustomAttribute<RequiresActiveAccountAttribute>());
        }
    }

    [Fact]
    public async Task Blocked_user_gets_403_user_blocked_when_creating_a_gift()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gblocked");
        var sender = await host.LoginAsync("gblocked-sender@example.az");

        using (var block = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/users/{UserId(sender)}/block", Access(shop.Shop.Admin), new { reason = "Saxta hesab" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        }

        // Access token hələ etibarlıdır, amma əmr hesabın vəziyyətini store-dan oxuyur.
        using var response = await PostIdempotentAsync(host, "/api/v1/gifts", Access(sender), GiftTestData.Voucher(shop));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("user_blocked", (await host.JsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(0, await host.Database.GetCollection<MongoDB.Bson.BsonDocument>("gifts")
            .CountDocumentsAsync(MongoDB.Driver.FilterDefinition<MongoDB.Bson.BsonDocument>.Empty, cancellationToken: Ct));
    }
}
