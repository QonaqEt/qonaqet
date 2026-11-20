using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Donations.Common;

/// <summary>
/// İanənin ödənişə başlanması: tətbiq <see cref="PaymentUrl"/>-i açır və <c>GET payments/{id}</c>-ni sorğulayır. <see cref="Refundable"/> və
/// <see cref="Notice"/> UI-nin ödənişdən <b>əvvəl</b> göstərdiyi mətndir: hovuz və kampaniya ianələri geri qaytarılmır.
/// </summary>
public sealed record DonationStartedDto(Guid DonationId, Guid PaymentId, Uri PaymentUrl, long AmountQepik, bool Refundable, string Notice);

public static class DonationRules
{
    public const int DonorNameMaxLength = 60;

    /// <summary>Onlayn ianələr (remote, hovuz, kampaniya) ödənişdən sonra geri qaytarılmır.</summary>
    public const string NonRefundableNotice = "İanə ödənişdən sonra geri qaytarılmır.";

    /// <summary>Hovuz/kampaniya ianəsinin sərhədləri: 1–1000 AZN.</summary>
    public const long MinAmountQepik = 1_00;

    public const long MaxAmountQepik = 1000_00;

    /// <summary>Remote ianənin ümumi dəyəri ən çox 1000 AZN.</summary>
    public const long MaxRemoteTotalQepik = 1000_00;

    /// <summary>
    /// Adlı göstərim: verilən ad, yoxdursa profilin adı; ad heç yoxdursa <c>donor_name_required</c>. Anonimdə ad saxlanılmır (boş sətir).
    /// </summary>
    public static Result<string> DisplayName(DonorDisplay display, string? requested, User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (display != DonorDisplay.Named)
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(requested) ? user.FirstName : requested.Trim();
        return string.IsNullOrWhiteSpace(name) ? DonationErrors.DonorNameRequired : name;
    }
}
