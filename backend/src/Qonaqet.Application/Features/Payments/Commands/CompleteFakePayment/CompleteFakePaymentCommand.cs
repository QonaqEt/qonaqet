using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Payments.Commands.CompleteFakePayment;

/// <summary>
/// Fake ödəniş səhifəsində "Ödə" / "İmtina et" (yalnız Development). Sifarişi Fake provayderdə yekunlaşdırır; bizim
/// <c>payments</c> statusu isə adi axınla (callback/yenidən yoxlama → Get Order) təsdiqlənir.
/// </summary>
[PublicRequest]
public sealed record CompleteFakePaymentCommand(string ProviderOrderId, bool Paid) : ICommand, INonTransactionalCommand;

public sealed class CompleteFakePaymentHandler(IFakeCheckout checkout) : ICommandHandler<CompleteFakePaymentCommand>
{
    public async Task<Result> Handle(CompleteFakePaymentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!checkout.IsEnabled)
        {
            return PaymentErrors.FakeCheckoutDisabled;
        }

        if (await checkout.FindAsync(request.ProviderOrderId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PaymentErrors.NotFound;
        }

        return await checkout.CompleteAsync(request.ProviderOrderId, request.Paid, cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : PaymentErrors.AlreadyCompleted;
    }
}
