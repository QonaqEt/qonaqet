using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary><c>payments</c> / <c>refunds</c>: status axını, unikal provayder sifarişi, append-only, qismən refund (E07-T03).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PaymentTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Started_payment_is_pending_and_visible_only_to_its_payer_and_admins()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var payer = await host.LoginAsync("payer@example.az");
        var stranger = await host.LoginAsync("stranger@example.az");
        var admin = await RolesTests.LoginAsAdminAsync(host, "pay-admin@example.az");

        var started = await StartAsync(host, UserId(payer), amount: 12_30);

        using (var own = await host.GetAsync($"/api/v1/payments/{started.PaymentId.Value}", Access(payer)))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            var json = await host.JsonAsync(own);
            Assert.Equal("Pending", json.GetProperty("status").GetString());
            Assert.Equal(12_30, json.GetProperty("amountQepik").GetInt64());
            Assert.Equal(started.PaymentUrl.ToString(), json.GetProperty("paymentUrl").GetString());
        }

        using (var other = await host.GetAsync($"/api/v1/payments/{started.PaymentId.Value}", Access(stranger)))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        using var adminView = await host.GetAsync($"/api/v1/admin/payments/{started.PaymentId.Value}", Access(admin));
        Assert.Equal(HttpStatusCode.OK, adminView.StatusCode);
        Assert.Equal("Fake", (await host.JsonAsync(adminView)).GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Provider_order_id_is_unique_and_payments_are_never_deleted()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var started = await StartAsync(host, Guid.CreateVersion7());
        var original = await LoadAsync(host, started.PaymentId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            repository.Add(Payment.Create(PaymentPurpose.Gift, Guid.CreateVersion7(), 100, "Fake", original.ProviderOrderId, new Uri("https://a.az/p"), "dup", new PaymentPayer(null), host.Clock.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync(Ct));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            var payment = await context.Set<Payment>().SingleAsync(p => p.Id == started.PaymentId, Ct);
            context.Remove(payment);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
        }

        Assert.NotNull(await LoadAsync(host, started.PaymentId));
    }

    [Fact]
    public async Task Partial_then_full_refund_tracks_refunded_amount_and_never_exceeds_the_payment()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "refund-admin@example.az"));
        var started = await StartAsync(host, Guid.CreateVersion7(), amount: 10_00);

        using (var notPaid = await RefundAsync(host, admin, started.PaymentId, 100))
        {
            Assert.Equal(HttpStatusCode.Conflict, notPaid.StatusCode);
            Assert.Equal("payment_not_confirmed", (await host.JsonAsync(notPaid)).GetProperty("code").GetString());
        }

        await MarkPaidAsync(host, started.PaymentId);

        using (var noKey = await RefundAsync(host, admin, started.PaymentId, 100, key: null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        }

        using (var partial = await RefundAsync(host, admin, started.PaymentId, 4_00))
        {
            Assert.Equal(HttpStatusCode.Created, partial.StatusCode);
        }

        var afterPartial = await LoadAsync(host, started.PaymentId);
        Assert.Equal(PaymentStatus.PartiallyRefunded, afterPartial.Status);
        Assert.Equal(4_00, afterPartial.RefundedAmount);

        using (var tooMuch = await RefundAsync(host, admin, started.PaymentId, 6_01))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
            Assert.Equal("refund_exceeds_payment", (await host.JsonAsync(tooMuch)).GetProperty("code").GetString());
        }

        using (var rest = await RefundAsync(host, admin, started.PaymentId, amount: null))
        {
            Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
        }

        var refunded = await LoadAsync(host, started.PaymentId);
        Assert.Equal(PaymentStatus.Refunded, refunded.Status);
        Assert.Equal(10_00, refunded.RefundedAmount);

        using var admView = await host.GetAsync($"/api/v1/admin/payments/{started.PaymentId.Value}", admin);
        var refunds = (await host.JsonAsync(admView)).GetProperty("refunds");
        Assert.Equal(2, refunds.GetArrayLength());
        Assert.All(refunds.EnumerateArray(), r => Assert.Equal("Succeeded", r.GetProperty("status").GetString()));

        Assert.Equal(["PaymentConfirmed", "PaymentRefunded", "PaymentRefunded"], await OutboxTypesAsync(host));
    }

    [Fact]
    public async Task Concurrent_refunds_never_exceed_the_payment()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "race-admin@example.az"));
        var started = await StartAsync(host, Guid.CreateVersion7(), amount: 10_00);
        await MarkPaidAsync(host, started.PaymentId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RefundAsync(host, admin, started.PaymentId, 3_00)));
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        var payment = await LoadAsync(host, started.PaymentId);
        Assert.True(payment.RefundedAmount <= payment.Amount);
        Assert.Equal(created * 3_00, payment.RefundedAmount);
        Assert.InRange(created, 1, 3);
    }

    [Fact]
    public async Task Refund_audit_and_payment_status_changes_are_recorded()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "audit-admin@example.az"));
        var started = await StartAsync(host, Guid.CreateVersion7(), amount: 5_00);
        await MarkPaidAsync(host, started.PaymentId);
        using (await RefundAsync(host, admin, started.PaymentId, 1_00))
        {
        }

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var audited = await context.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(Payment) || a.EntityType == nameof(Refund))
            .Select(a => a.EntityType + ":" + a.Action).ToListAsync(Ct);
        Assert.Contains("Payment:Create", audited);
        Assert.Contains("Payment:Update", audited);
        Assert.Contains("Refund:Create", audited);
    }
}
