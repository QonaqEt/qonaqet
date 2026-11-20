using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Application.Features.Gifts.Commands.CreateGift;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Files;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Corporate.Commands.CorporateOrders;

/// <summary>
/// <c>POST corporate/orders</c>: korporativ hədiyyə sifarişinin qaralaması — şablon (məhsullar, biznes vauçeri və ya şəbəkə vauçeri; tema həmişə
/// <c>Corporate</c>), mesaj, çatdırılma vaxtı, müddət bitməsi. Bir hədiyyənin qiyməti serverdə hesablanır (<see cref="GiftPricing"/>).
/// </summary>
[RequiresRole(RoleNames.CorporateUser)]
[RequiresActiveAccount]
public sealed record CreateCorporateOrderCommand(GiftSelection What, string? Message, DateTimeOffset? DeliverAt, GiftExpiryAction ExpiryAction)
    : ICommand<CorporateOrderDto>;

/// <summary>
/// <c>POST corporate/orders/{id}/recipients</c>: alıcı siyahısı (CSV <c>name,email</c>, ≤ 5,000 sətir). Hər səhv sətir nömrəsi və səbəbi ilə
/// qaytarılır (boş ad, səhv email, təkrar email, çox sətir); səhv varsa sifariş dəyişmir. Uğurda fayl saxlanılır, siyahı və ümumi məbləğ
/// (qiymət × alıcı sayı) yenilənir.
/// </summary>
[RequiresRole(RoleNames.CorporateUser)]
[RequiresActiveAccount]
public sealed record UploadCorporateRecipientsCommand(CorporateGiftOrderId OrderId, ReadOnlyMemory<byte> Csv, string? FileName) : ICommand<RecipientsReportDto>;

/// <summary><c>POST corporate/orders/{id}/cancel</c>: ödənilməmiş sifarişin ləğvi.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record CancelCorporateOrderCommand(CorporateGiftOrderId OrderId) : ICommand<CorporateOrderDto>;

/// <summary>CSV yoxlamasının hesabatı. <see cref="Accepted"/> = <c>false</c> olduqda sifariş dəyişməyib.</summary>
public sealed record RecipientsReportDto(bool Accepted, int RecipientCount, long TotalQepik, IReadOnlyList<RecipientErrorDto> Errors);

/// <summary>Sətir nömrəsi faylda olduğu kimidir (başlıq 1-ci sətirdir).</summary>
public sealed record RecipientErrorDto(int Line, string Code, string Message);

public sealed class CreateCorporateOrderValidator : Validator<CreateCorporateOrderCommand>
{
    public CreateCorporateOrderValidator()
    {
        Rule("what", x => x.What is not null, GiftErrors.ItemsInvalid.Code, GiftErrors.ItemsInvalid.Message);
        RuleFor<string?>(x => x.Message).MaxLength(GiftRules.MessageMaxLength);
        Rule("expiryAction", x => Enum.IsDefined(x.ExpiryAction), "invalid_expiry_action", "Müddət bitməsi seçimi düzgün deyil.");
    }
}

public sealed class CreateCorporateOrderHandler(
    CorporateAccess access, GiftPricing pricing, GiftExpiryPools expiryPools, ICorporateGiftOrderRepository orders, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateCorporateOrderCommand, CorporateOrderDto>
{
    public async Task<Result<CorporateOrderDto>> Handle(CreateCorporateOrderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        var now = clock.UtcNow;
        var deliverAt = request.DeliverAt?.ToUniversalTime() ?? now;
        if (deliverAt > now + GiftRules.MaxScheduleAhead)
        {
            return GiftErrors.DeliverAtOutOfRange;
        }

        var content = await pricing.PriceAsync(request.What, cancellationToken).ConfigureAwait(false);
        if (content.IsFailure)
        {
            return content.Error;
        }

        var expiry = await expiryPools.PlanAsync(request.ExpiryAction, cancellationToken).ConfigureAwait(false);
        if (expiry.IsFailure)
        {
            return expiry.Error;
        }

        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var order = CorporateGiftOrder.Create(corporate.Value.Id, userId, content.Value, request.Message, deliverAt, expiry.Value, now);
        await orders.AddAsync(order, cancellationToken).ConfigureAwait(false);
        return order.ToDto();
    }
}

public sealed class UploadCorporateRecipientsHandler(
    CorporateAccess access, ICsvService csv, FileUploader uploader, ICorporateGiftOrderRepository orders, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UploadCorporateRecipientsCommand, RecipientsReportDto>
{
    public async Task<Result<RecipientsReportDto>> Handle(UploadCorporateRecipientsCommand request, CancellationToken cancellationToken)
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

        var parsed = await RecipientsCsv.ParseAsync(csv, request.Csv, cancellationToken).ConfigureAwait(false);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var (recipients, errors) = parsed.Value;
        if (errors.Count > 0)
        {
            return new RecipientsReportDto(false, recipients.Count, 0, errors);
        }

        var userId = currentUser.ActorId;
        var stored = await uploader.UploadAsync(
            new FileUpload(FilePurpose.RecipientCsv, "text/csv", request.Csv),
            FileOwnerType.CorporateGiftOrder, order.Id.Value, userId, cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        order.SetRecipients(recipients, stored.Value.Id.Value, clock.UtcNow, userId);
        await orders.UpdateAsync(order, cancellationToken).ConfigureAwait(false);
        return new RecipientsReportDto(true, order.RecipientCount, order.TotalQepik, []);
    }
}

public sealed class CancelCorporateOrderHandler(CorporateAccess access, ICorporateGiftOrderRepository orders, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CancelCorporateOrderCommand, CorporateOrderDto>
{
    public async Task<Result<CorporateOrderDto>> Handle(CancelCorporateOrderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await access.RequireOrderAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var order = found.Value.Order;
        if (order.Status is not (CorporateOrderStatus.Draft or CorporateOrderStatus.AwaitingPayment)
            || (order.Status == CorporateOrderStatus.AwaitingPayment && order.PaymentMethod == CorporatePaymentMethod.Card))
        {
            // Kart ödənişi davam edir: nəticəsi (consumer) gözlənilir.
            return CorporateErrors.NotCancellable;
        }

        var expected = order.Status;
        order.Cancel(clock.UtcNow, currentUser.ActorId);
        return await orders.TryReplaceAsync(order, expected, order.SentCount, cancellationToken).ConfigureAwait(false)
            ? order.ToDto()
            : CorporateErrors.NotCancellable;
    }
}

/// <summary>Alıcı CSV-sinin oxunması və sətir-sətir yoxlanması (öz RFC 4180 oxuyucumuzla).</summary>
public static class RecipientsCsv
{
    public static async Task<Result<(IReadOnlyList<CorporateRecipient> Recipients, IReadOnlyList<RecipientErrorDto> Errors)>> ParseAsync(
        ICsvService csv, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csv);
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        var document = await csv.ReadAsync(stream, new CsvReadOptions { MaxRows = CorporateGiftOrder.MaxRecipients, MaxFieldLength = 254 }, cancellationToken)
            .ConfigureAwait(false);
        var header = document.Header.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var nameIndex = header.IndexOf("name");
        var emailIndex = header.IndexOf("email");
        if (nameIndex < 0 || emailIndex < 0)
        {
            return CorporateErrors.CsvInvalid;
        }

        var errors = document.Errors.Select(e => new RecipientErrorDto(e.LineNumber, e.Code, e.Message)).ToList();
        var recipients = new List<CorporateRecipient>(document.Rows.Count);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.Rows)
        {
            var name = row.Fields[nameIndex].Trim();
            var email = row.Fields[emailIndex].Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Length > GiftRules.RecipientNameMaxLength)
            {
                errors.Add(new RecipientErrorDto(row.LineNumber, "name_invalid", "Ad boşdur və ya 100 simvoldan uzundur."));
                continue;
            }

            if (!IsEmail(email))
            {
                errors.Add(new RecipientErrorDto(row.LineNumber, "email_invalid", "Email ünvanı düzgün deyil."));
                continue;
            }

            if (seen.TryGetValue(email, out var firstLine))
            {
                errors.Add(new RecipientErrorDto(row.LineNumber, "email_duplicate", $"Bu email {firstLine}-ci sətirdə artıq var."));
                continue;
            }

            seen[email] = row.LineNumber;
            recipients.Add(new CorporateRecipient(name, email));
        }

        if (recipients.Count == 0 && errors.Count == 0)
        {
            errors.Add(new RecipientErrorDto(1, "empty", "Faylda alıcı yoxdur."));
        }

        return (recipients, errors.OrderBy(e => e.Line).ToList());
    }

    /// <summary>Sadə, sərt yoxlama: bir <c>@</c>, boşluqsuz, domendə nöqtə.</summary>
    public static bool IsEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace)
            && value[(at + 1)..].Contains('.', StringComparison.Ordinal) && !value.EndsWith('.') && value.Length <= 254;
    }
}
