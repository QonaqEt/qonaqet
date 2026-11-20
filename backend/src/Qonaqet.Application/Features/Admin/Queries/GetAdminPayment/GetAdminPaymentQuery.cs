using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.GetAdminPayment;

/// <summary><c>GET admin/payments/{id}</c>: provayder məlumatı və refundlar.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetAdminPaymentQuery(PaymentId PaymentId) : IQuery<AdminPaymentDto>;

public sealed class GetAdminPaymentHandler(IPaymentRepository payments) : IQueryHandler<GetAdminPaymentQuery, AdminPaymentDto>
{
    public async Task<Result<AdminPaymentDto>> Handle(GetAdminPaymentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payment = await payments.GetAsync(request.PaymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        var refunds = await payments.ListRefundsAsync(request.PaymentId, cancellationToken).ConfigureAwait(false);
        return new AdminPaymentDto(
            payment.ToDto(),
            payment.Provider,
            payment.ProviderOrderId,
            payment.PayerUserId,
            payment.CorporateId,
            payment.PartnerClientId,
            refunds.Select(r => r.ToDto()).ToArray());
    }
}
