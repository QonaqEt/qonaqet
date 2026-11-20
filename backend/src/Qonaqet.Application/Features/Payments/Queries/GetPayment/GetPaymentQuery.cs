using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Payments;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Payments.Queries.GetPayment;

/// <summary><c>GET payments/{id}</c>: yalnız ödəyici (və admin). Başqasının ödənişi 404 kimi görünür.</summary>
public sealed record GetPaymentQuery(PaymentId PaymentId) : IQuery<PaymentDto>;

public sealed class GetPaymentHandler(IPaymentRepository payments, ICurrentUser currentUser) : IQueryHandler<GetPaymentQuery, PaymentDto>
{
    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payment = await payments.GetAsync(request.PaymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null || (payment.PayerUserId != currentUser.ActorId && !currentUser.IsAdmin()))
        {
            return PaymentErrors.NotFound;
        }

        return payment.ToDto();
    }
}
