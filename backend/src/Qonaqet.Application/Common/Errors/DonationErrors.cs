using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>İanələrin (remote, hovuz, kampaniya) sabit xəta kodları.</summary>
public static class DonationErrors
{
    public static readonly Error DonorNameRequired = Error.Validation("donor_name_required", "Adlı ianə üçün ad lazımdır.");

    public static readonly Error ItemUnavailable =
        Error.Validation("donation_item_unavailable", "Məhsul tapılmadı, aktiv deyil və ya bu biznesə aid deyil.");

    public static readonly Error AmountOutOfRange = Error.Validation("donation_amount_invalid", "İanə məbləği 1–1000 AZN arasında olmalıdır.");

    public static readonly Error BusinessUnavailable = Error.Conflict("business_not_active", "Biznes hazırda aktiv deyil.");

    public static readonly Error PoolNotFound = Error.NotFound("pool_not_found", "Hovuz tapılmadı.");

    public static readonly Error CampaignNotFound = Error.NotFound("campaign_not_found", "Kampaniya tapılmadı.");

    public static readonly Error CampaignNotActive = Error.Conflict("campaign_not_active", "Kampaniya hazırda aktiv deyil.");
}
