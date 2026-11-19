using System.Globalization;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Businesses.Commands.ChangeBusinessSettings;

/// <summary>
/// <c>PATCH business/{businessId}/settings</c>: hovuz və şəbəkə voucher-lərinin qəbulu, biznes voucher-inin min/max həddi
/// (qəpik). Yalnız göndərilən sahələr dəyişir (<c>null</c> = toxunma); nəticədə min ≤ max olmalıdır. Yalnız sahib və admin.
/// Dəyişiklik (əvvəl/sonra) audit log-a düşür.
/// </summary>
[RequiresActiveAccount]
public sealed record ChangeBusinessSettingsCommand(
    BusinessId BusinessId, bool? AcceptsPool, bool? AcceptsNetworkVouchers, long? VoucherMinQepik, long? VoucherMaxQepik) : ICommand<BusinessDto>;

public sealed class ChangeBusinessSettingsValidator : Validator<ChangeBusinessSettingsCommand>
{
    public ChangeBusinessSettingsValidator()
    {
        RuleFor(x => x.VoucherMinQepik).Must(v => v is null || InBounds(v.Value), "voucher_limit_invalid", "Voucher həddi 1 ilə 1000 AZN arasında olmalıdır.");
        RuleFor(x => x.VoucherMaxQepik).Must(v => v is null || InBounds(v.Value), "voucher_limit_invalid", "Voucher həddi 1 ilə 1000 AZN arasında olmalıdır.");
        Rule("voucherMinQepik", x => x.VoucherMinQepik is null || x.VoucherMaxQepik is null || x.VoucherMinQepik <= x.VoucherMaxQepik,
            VoucherRangeInvalidCode, "Minimum məbləğ maksimumdan böyük ola bilməz.");
    }

    public const string VoucherRangeInvalidCode = "voucher_range_invalid";

    private static bool InBounds(long value) => value is >= Business.VoucherLimitFloorQepik and <= Business.VoucherLimitCeilingQepik;
}

public sealed class ChangeBusinessSettingsHandler(
    BusinessAccess access, IBusinessRepository businesses, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<ChangeBusinessSettingsCommand, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(ChangeBusinessSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var business = grant.Value.Business;
        var min = request.VoucherMinQepik ?? business.VoucherMinQepik;
        var max = request.VoucherMaxQepik ?? business.VoucherMaxQepik;
        if (min > max)
        {
            // Bir sahə göndərilib, digəri mövcud dəyərlə toqquşur.
            return new ValidationError([new ValidationFailure("voucherMinQepik", ChangeBusinessSettingsValidator.VoucherRangeInvalidCode, "Minimum məbləğ maksimumdan böyük ola bilməz.")]);
        }

        var before = Snapshot(business);
        var now = clock.UtcNow;
        business.ChangeSettings(
            request.AcceptsPool ?? business.AcceptsPool, request.AcceptsNetworkVouchers ?? business.AcceptsNetworkVouchers, min, max, now, currentUser.ActorId);
        await businesses.UpdateAsync(business, cancellationToken).ConfigureAwait(false);

        var after = Snapshot(business);
        var data = new Dictionary<string, string?>();
        foreach (var (key, value) in after.Where(kv => before[kv.Key] != kv.Value))
        {
            data[$"{key}.before"] = before[key];
            data[$"{key}.after"] = value;
        }

        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.ChangeSettings, nameof(Business), business.Id.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return business.ToDto(grant.Value.SeesFinancials);
    }

    private static Dictionary<string, string> Snapshot(Business business) => new()
    {
        ["acceptsPool"] = business.AcceptsPool ? "true" : "false",
        ["acceptsNetworkVouchers"] = business.AcceptsNetworkVouchers ? "true" : "false",
        ["voucherMinQepik"] = business.VoucherMinQepik.ToString(CultureInfo.InvariantCulture),
        ["voucherMaxQepik"] = business.VoucherMaxQepik.ToString(CultureInfo.InvariantCulture),
    };
}
