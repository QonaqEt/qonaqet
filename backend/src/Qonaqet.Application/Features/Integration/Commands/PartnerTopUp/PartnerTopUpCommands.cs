using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerTopUp;

/// <summary>
/// <c>POST /partner/v1/balance/top-ups</c> (<c>gifts:issue</c>): partnyor balansını kartla artırır — <c>PartnerTopUp</c> ödənişi başlayır
/// (sandbox client-də Fake provayder). Təsdiqdə ödəniş consumer-i balansa <c>PartnerTopUp</c> ledger yazısı əlavə edir.
/// </summary>
[RequiresRole(PartnerScopes.GiftsIssue)]
public sealed record StartPartnerTopUpCommand(long AmountQepik) : ICommand<PartnerTopUpStartedDto>, INonTransactionalCommand;

/// <summary>
/// <c>POST admin/integration-clients/{id}/top-ups</c>: admin bank köçürməsini bank referansı ilə təsdiqləyir → balansa <c>PartnerTopUp</c>.
/// Audit log-a düşür.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ConfirmPartnerTopUpCommand(IntegrationClientId ClientId, long AmountQepik, string BankReference)
    : ICommand<PartnerTopUpConfirmedDto>, IPostgresCommand, IIdempotentCommand;

public sealed record PartnerTopUpStartedDto(Guid PaymentId, Uri PaymentUrl, long AmountQepik);

public sealed record PartnerTopUpConfirmedDto(Guid ClientId, long AmountQepik, long BalanceQepik);

public static class PartnerTopUps
{
    public const long MinQepik = 10_00;

    public const long MaxQepik = 100_000_00;

    public const int BankReferenceMaxLength = 100;

    public static bool IsValidAmount(long amount) => amount is >= MinQepik and <= MaxQepik;
}

public sealed class StartPartnerTopUpValidator : Validator<StartPartnerTopUpCommand>
{
    public StartPartnerTopUpValidator() =>
        Rule("amountQepik", x => PartnerTopUps.IsValidAmount(x.AmountQepik), IntegrationErrors.TopUpInvalid.Code, IntegrationErrors.TopUpInvalid.Message);
}

public sealed class ConfirmPartnerTopUpValidator : Validator<ConfirmPartnerTopUpCommand>
{
    public ConfirmPartnerTopUpValidator()
    {
        Rule("amountQepik", x => PartnerTopUps.IsValidAmount(x.AmountQepik), IntegrationErrors.TopUpInvalid.Code, IntegrationErrors.TopUpInvalid.Message);
        Rule(
            "bankReference",
            x => !string.IsNullOrWhiteSpace(x.BankReference) && x.BankReference.Length <= PartnerTopUps.BankReferenceMaxLength,
            "bank_reference_required",
            "Bank referansı məcburidir (≤ 100 simvol).");
    }
}

public sealed class StartPartnerTopUpHandler(PartnerAccess access, PaymentStarter payments) : ICommandHandler<StartPartnerTopUpCommand, PartnerTopUpStartedDto>
{
    public async Task<Result<PartnerTopUpStartedDto>> Handle(StartPartnerTopUpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var partner = client.Value;
        var started = await payments.StartAsync(
            new PaymentStart(
                PaymentPurpose.PartnerTopUp, partner.Id.Value, request.AmountQepik, "QONAQET · Partnyor balansı", new PaymentPayer(null, null, partner.Id.Value),
                partner.IsSandbox),
            cancellationToken).ConfigureAwait(false);
        return started.IsFailure
            ? started.Error
            : new PartnerTopUpStartedDto(started.Value.PaymentId.Value, started.Value.PaymentUrl, request.AmountQepik);
    }
}

public sealed class ConfirmPartnerTopUpHandler(
    IIntegrationClientRepository clients, ILedgerRepository ledger, IOutbox outbox, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ConfirmPartnerTopUpCommand, PartnerTopUpConfirmedDto>
{
    public const string AuditAction = "PartnerTopUpConfirmBankTransfer";

    public async Task<Result<PartnerTopUpConfirmedDto>> Handle(ConfirmPartnerTopUpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await clients.GetAsync(request.ClientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return IntegrationErrors.ClientNotFound;
        }

        var now = clock.UtcNow;
        var account = LedgerAccount.PartnerClient(client.Id.Value);
        var topUpId = Guid.CreateVersion7();
        await ledger.LockAccountAsync(account, cancellationToken).ConfigureAwait(false);
        ledger.Add(LedgerEntry.Post(account, LedgerEntryType.PartnerTopUp, request.AmountQepik, "BankTransfer", topUpId, topUpId, "partner-topup", now));
        await ledger.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var balance = await ledger.BalanceAsync(account, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", AuditAction, nameof(IntegrationClient), client.Id.Value,
                new Dictionary<string, string?>
                {
                    ["amountQepik"] = request.AmountQepik.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["bankReference"] = request.BankReference.Trim(),
                })],
            cancellationToken).ConfigureAwait(false);
        return new PartnerTopUpConfirmedDto(client.Id.Value, request.AmountQepik, balance);
    }
}
