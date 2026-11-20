using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Social;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Ledger və komissiya (E07-T05): idempotent consumer, balans = SUM(amount), komissiya yalnız onlayn ödənişlidə.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class LedgerTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Online_paid_claim_redemption_writes_credit_and_commission_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        var message = Message(ClaimRedeemed(business, amount: 8_00, commissionable: true));

        Assert.True(await ProcessAsync(host, message));
        Assert.False(await ProcessAsync(host, message)); // ikinci çatdırılma

        Assert.Equal(8_00 - 56, await BalanceAsync(host, LedgerAccount.Business(business)));
        Assert.Equal(56, await BalanceAsync(host, LedgerAccount.Platform));
        var types = await EntriesAsync(host, business);
        Assert.Equal([LedgerEntryType.RedemptionCredit, LedgerEntryType.Commission], types.Order());
    }

    [Fact]
    public async Task Daily_and_suspended_redemptions_write_no_money()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var business = Guid.CreateVersion7();

        Assert.True(await ProcessAsync(host, Message(ClaimRedeemed(business, amount: null, commissionable: false))));

        Assert.Empty(await EntriesAsync(host, business));
        Assert.Equal(0, await BalanceAsync(host, LedgerAccount.Business(business)));
    }

    [Fact]
    public async Task Same_event_through_another_consumer_name_is_still_blocked_by_the_unique_source_event_id()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        var message = Message(ClaimRedeemed(business, amount: 5_00, commissionable: true));
        Assert.True(await ProcessAsync(host, message));

        // İnbox-dan yan keçən (məs. əl ilə təkrar) emal unikal source_event_id ilə dayanır və heç nə yazmır.
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await new LedgerConsumer(Options.Create(new LedgerOptions())).HandleAsync(message, context, Ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));

        Assert.Equal(5_00 - 35, await BalanceAsync(host, LedgerAccount.Business(business)));
    }

    [Fact]
    public async Task Commission_percent_comes_from_configuration()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();

        await new LedgerConsumer(Options.Create(new LedgerOptions { CommissionPercent = 10 }))
            .HandleAsync(Message(ClaimRedeemed(business, amount: 20_00, commissionable: true)), context, Ct);
        await context.SaveChangesAsync(Ct);

        Assert.Equal(18_00, await BalanceAsync(host, LedgerAccount.Business(business)));
    }

    [Fact]
    public async Task Refund_event_posts_a_refund_and_ledger_rows_cannot_be_deleted()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var paymentId = PaymentId.From(Guid.CreateVersion7());
        var refunded = new PaymentRefunded(paymentId, RefundId.From(Guid.CreateVersion7()), PaymentPurpose.Gift, Guid.CreateVersion7(), 4_00, 4_00, host.Clock.UtcNow);

        Assert.True(await ProcessAsync(host, Message(refunded)));

        Assert.Equal(-4_00, await BalanceAsync(host, LedgerAccount.Platform));
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var entry = await context.Set<LedgerEntry>().SingleAsync(Ct);
        Assert.Equal("Payment", entry.RefType);
        Assert.Equal(paymentId.Value, entry.RefId);
        context.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Business_ledger_shows_the_balance_to_the_owner_only()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await SocialTestData.ShopAsync(host, "ledger");
        var business = Guid.Parse(shop.BusinessId);
        Assert.True(await ProcessAsync(host, Message(ClaimRedeemed(business, amount: 10_00, commissionable: true))));
        Assert.True(await ProcessAsync(host, Message(ClaimRedeemed(business, amount: 3_00, commissionable: true))));

        using (var owner = await host.GetAsync($"/api/v1/business/{business}/ledger?pageSize=2", SocialTestData.Access(shop.Owner)))
        {
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
            var json = await host.JsonAsync(owner);
            Assert.Equal(13_00 - 70 - 21, json.GetProperty("balanceQepik").GetInt64());
            Assert.Equal(4, json.GetProperty("total").GetInt64());
            Assert.Equal(2, json.GetProperty("items").GetArrayLength());
        }

        using (var staff = await host.GetAsync($"/api/v1/business/{business}/ledger", SocialTestData.Access(shop.Staff)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        }

        var stranger = await host.LoginAsync("ledger-stranger@example.az");
        using var other = await host.GetAsync($"/api/v1/business/{business}/ledger", Access(stranger));
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    internal static ClaimRedeemed ClaimRedeemed(Guid businessId, long? amount, bool commissionable) => new(
        ClaimId.From(Guid.CreateVersion7()),
        BusinessId.From(businessId),
        commissionable ? ClaimSource.Pool : ClaimSource.Offer,
        null,
        commissionable ? null : OfferType.BusinessDaily,
        commissionable ? PoolId.From(Guid.CreateVersion7()) : null,
        amount,
        commissionable,
        Guid.CreateVersion7(),
        new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    internal static IntegrationMessage Message(IDomainEvent domainEvent) =>
        new(domainEvent.EventId, OutboxSerializer.TypeName(domainEvent), OutboxSerializer.Serialize(domainEvent));

    internal static async Task<bool> ProcessAsync(ApiTestHost host, IntegrationMessage message)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await new InboxProcessor(context).ProcessAsync(new LedgerConsumer(Options.Create(new LedgerOptions())), message, host.Clock.UtcNow, Ct);
    }

    internal static async Task<long> BalanceAsync(ApiTestHost host, LedgerAccount account)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILedgerRepository>().BalanceAsync(account, Ct);
    }

    private static async Task<IReadOnlyList<LedgerEntryType>> EntriesAsync(ApiTestHost host, Guid businessId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await context.Set<LedgerEntry>().AsNoTracking().Where(e => e.AccountId == businessId).Select(e => e.Type).ToListAsync(Ct);
    }
}
