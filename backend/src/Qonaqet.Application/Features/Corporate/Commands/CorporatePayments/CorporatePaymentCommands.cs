using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Corporate.Commands.AdminCorporates;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Corporate.Commands.CorporatePayments;

/// <summary>
/// <c>POST corporate/orders/{id}/pay</c> (<c>Idempotency-Key</c> məcburi): kartla — Payriff ödənişi başladılır (məbləğ = qiymət × alıcı sayı,
/// serverdə); bank köçürməsi ilə — sifariş <c>AwaitingPayment</c>-də admin təsdiqini gözləyir (istifadə olunmayan dəyər yalnız hovuza
/// bağışlana bilər, çünki köçürmə provayderdən refund olunmur).
/// </summary>
[RequiresRole(RoleNames.CorporateUser)]
[RequiresActiveAccount]
public sealed record PayCorporateOrderCommand(CorporateGiftOrderId OrderId, CorporatePaymentMethod Method)
    : ICommand<CorporatePaymentDto>, INonTransactionalCommand, IIdempotentCommand;

/// <summary>Ödəniş başladı: kartda <see cref="PaymentUrl"/>; bank köçürməsində təyinatda yazılacaq <see cref="BankTransferReference"/>.</summary>
public sealed record CorporatePaymentDto(Guid OrderId, CorporatePaymentMethod Method, long AmountQepik, Guid? PaymentId, Uri? PaymentUrl, string? BankTransferReference);

/// <summary>
/// <c>POST admin/corporate-orders/{id}/confirm-bank-transfer</c>: admin bank referansı ilə köçürməni təsdiqləyir → <c>Paid</c> (fan-out başlayır).
/// Status keçidi, <c>CorporateOrderPaid</c> və audit qeydi bir Mongo tranzaksiyasındadır.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ConfirmCorporateBankTransferCommand(CorporateGiftOrderId OrderId, string BankReference) : ICommand<CorporateOrderDto>;

public sealed class PayCorporateOrderValidator : Validator<PayCorporateOrderCommand>
{
    public PayCorporateOrderValidator() =>
        Rule("method", x => Enum.IsDefined(x.Method), "invalid_payment_method", "Ödəniş üsulu düzgün deyil.");
}

public sealed class ConfirmCorporateBankTransferValidator : Validator<ConfirmCorporateBankTransferCommand>
{
    public ConfirmCorporateBankTransferValidator() => RuleFor<string?>(x => x.BankReference).NotEmpty().MaxLength(100);
}

public sealed class PayCorporateOrderHandler(
    CorporateAccess access, ICorporateGiftOrderRepository orders, PaymentStarter payments, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<PayCorporateOrderCommand, CorporatePaymentDto>
{
    public async Task<Result<CorporatePaymentDto>> Handle(PayCorporateOrderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await access.RequireOrderAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var order = found.Value.Order;
        if (order.Status != CorporateOrderStatus.Draft)
        {
            return CorporateErrors.OrderNotEditable;
        }

        if (order.RecipientCount == 0)
        {
            return CorporateErrors.RecipientsRequired;
        }

        var userId = currentUser.ActorId;
        var now = clock.UtcNow;
        if (request.Method == CorporatePaymentMethod.BankTransfer)
        {
            if (order.ExpiryAction != GiftExpiryAction.DonateToPool)
            {
                return CorporateErrors.BankTransferRequiresDonation;
            }

            order.ChooseBankTransfer(now, userId);
            if (!await orders.TryReplaceAsync(order, CorporateOrderStatus.Draft, 0, cancellationToken).ConfigureAwait(false))
            {
                return CorporateErrors.OrderNotEditable;
            }

            return new CorporatePaymentDto(order.Id.Value, request.Method, order.TotalQepik, null, null, BankReference(order.Id));
        }

        // Provayderə gedən təsvirdə şirkətin və alıcıların adı yoxdur.
        var started = await payments.StartAsync(
            new PaymentStart(PaymentPurpose.CorporateGiftOrder, order.Id.Value, order.TotalQepik, "QONAQET · Korporativ hədiyyələr",
                new PaymentPayer(userId, order.CorporateId.Value)),
            cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        order.StartCardPayment(started.Value.PaymentId.Value, now, userId);
        if (!await orders.TryReplaceAsync(order, CorporateOrderStatus.Draft, 0, cancellationToken).ConfigureAwait(false))
        {
            // Ödənilməmiş sifariş 30 dəqiqədən sonra Expired olur.
            return CorporateErrors.OrderNotEditable;
        }

        return new CorporatePaymentDto(order.Id.Value, request.Method, order.TotalQepik, started.Value.PaymentId.Value, started.Value.PaymentUrl, null);
    }

    /// <summary>Köçürmənin təyinatı: sifarişin qısa, sabit referansı.</summary>
    public static string BankReference(CorporateGiftOrderId orderId) => $"QONAQET-{orderId.Value.ToString("N")[..12].ToUpperInvariant()}";
}

public sealed class ConfirmCorporateBankTransferHandler(ICorporateGiftOrderRepository orders, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<ConfirmCorporateBankTransferCommand, CorporateOrderDto>
{
    public async Task<Result<CorporateOrderDto>> Handle(ConfirmCorporateBankTransferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await orders.GetAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return CorporateErrors.OrderNotFound;
        }

        if (order.Status != CorporateOrderStatus.AwaitingPayment || order.PaymentMethod != CorporatePaymentMethod.BankTransfer)
        {
            return CorporateErrors.NotAwaitingBankTransfer;
        }

        var now = clock.UtcNow;
        order.MarkPaid(request.BankReference, now, currentUser.ActorId);
        if (!await orders.TryReplaceAsync(order, CorporateOrderStatus.AwaitingPayment, 0, cancellationToken).ConfigureAwait(false))
        {
            return CorporateErrors.NotAwaitingBankTransfer;
        }

        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", CorporateAuditActions.ConfirmBankTransfer, nameof(CorporateGiftOrder), order.Id.Value,
                new Dictionary<string, string?>
                {
                    ["bankReference"] = order.BankReference,
                    ["amountQepik"] = order.TotalQepik.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })],
            cancellationToken).ConfigureAwait(false);
        return order.ToDto();
    }
}
