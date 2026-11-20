using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Payments.Queries.GetFakeCheckout;

/// <summary>Fake ödəniş səhifəsinin məlumatı (yalnız Development).</summary>
[PublicRequest]
public sealed record GetFakeCheckoutQuery(string ProviderOrderId) : IQuery<FakeOrderView>;

public sealed class GetFakeCheckoutHandler(IFakeCheckout checkout) : IQueryHandler<GetFakeCheckoutQuery, FakeOrderView>
{
    public async Task<Result<FakeOrderView>> Handle(GetFakeCheckoutQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!checkout.IsEnabled)
        {
            return PaymentErrors.FakeCheckoutDisabled;
        }

        var order = await checkout.FindAsync(request.ProviderOrderId, cancellationToken).ConfigureAwait(false);
        return order is null ? PaymentErrors.NotFound : order;
    }
}
