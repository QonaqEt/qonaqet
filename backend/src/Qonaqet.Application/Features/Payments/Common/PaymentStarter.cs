using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Payments;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Payments.Common;

/// <summary>Məqsəd endpoint-inin (hədiyyə, ianə, korporativ sifariş …) serverdə hesabladığı ödəniş.</summary>
/// <param name="Description">Provayderə gedən mətn: şəxsi məlumat (ad, email, mesaj) olmamalıdır.</param>
/// <param name="Sandbox">Sandbox partnyoru: həmişə Fake provayder.</param>
public sealed record PaymentStart(PaymentPurpose Purpose, Guid ReferenceId, long AmountQepik, string Description, PaymentPayer Payer, bool Sandbox = false);

public sealed record StartedPayment(PaymentId PaymentId, Uri PaymentUrl);

/// <summary>
/// Ödəniş başladır: provayderdə sifariş yaradır və <c>payments</c>-ə <c>Pending</c> sətri yazır. <c>POST payments</c> yoxdur — bunu yalnız
/// məqsəd əmrləri çağırır və məbləği həmişə özləri (serverdə) hesablayır. Sətir dərhal saxlanılır (açıq Postgres tranzaksiyası varsa onun
/// içində): məqsədin Mongo yazısı sonra uğursuz olsa, ödənilməyən sifariş 30 dəqiqədən sonra <c>Expired</c> olur.
/// </summary>
public sealed class PaymentStarter(IPaymentGatewayRegistry gateways, IPaymentRepository payments, IAppUrls urls, IClock clock)
{
    public async Task<Result<StartedPayment>> StartAsync(PaymentStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        Guard.Positive(start.AmountQepik);
        var gateway = start.Sandbox ? gateways.Sandbox : gateways.Default;

        GatewayOrder order;
        try
        {
            order = await gateway.CreateOrderAsync(new GatewayOrderRequest(start.AmountQepik, start.Description, urls.PaymentCallback), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PaymentGatewayException)
        {
            return PaymentErrors.GatewayUnavailable;
        }

        var payment = Payment.Create(
            start.Purpose, start.ReferenceId, start.AmountQepik, gateway.Name, order.ProviderOrderId, order.PaymentUrl, start.Description, start.Payer, clock.UtcNow);
        payments.Add(payment);
        await payments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new StartedPayment(payment.Id, order.PaymentUrl);
    }
}
