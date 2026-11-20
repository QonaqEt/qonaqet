using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.RefundPayment;

/// <summary>
/// <c>POST admin/payments/{id}/refunds</c>: tam (<see cref="AmountQepik"/> = <c>null</c>) və ya qismən refund. Pul hərəkətidir:
/// <c>Idempotency-Key</c> məcburidir.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RefundPaymentCommand(PaymentId PaymentId, long? AmountQepik, string Reason)
    : ICommand<Guid>, IPostgresCommand, INonTransactionalCommand, IIdempotentCommand;

public sealed class RefundPaymentValidator : Validator<RefundPaymentCommand>
{
    public RefundPaymentValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(Refund.MaxReasonLength);
        Rule("amountQepik", x => x.AmountQepik is null or > 0, "invalid_amount", "Məbləğ müsbət olmalıdır.");
    }
}

public sealed class RefundPaymentHandler(PaymentRefunds refunds, ICurrentUser currentUser) : ICommandHandler<RefundPaymentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await refunds.RefundAsync(request.PaymentId, request.AmountQepik, request.Reason, currentUser.ActorId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value.Value : result.Error;
    }
}
