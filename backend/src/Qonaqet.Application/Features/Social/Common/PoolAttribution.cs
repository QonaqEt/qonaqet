using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Hovuz claim-ində alıcıya göstərilən ianəçi mətni və atribusiya olunan ianə. <see cref="UserId"/> və <see cref="CorporateId"/> yalnız reytinq
/// xalı üçündür (alıcıya verilmir): ianəni edən istifadəçi və hovuz şirkətin CSR kampaniyasınındırsa həmin şirkət.
/// </summary>
public sealed record PoolDonor(string Text, Guid? DonationId, Guid? UserId, Guid? CorporateId);

/// <summary>
/// Hovuz claim-inin donor atribusiyası (proporsional): claim hovuzun <b>ən köhnə istifadə olunmamış</b> təsdiqlənmiş ianəsindən maliyyələşir.
/// Həmin ianə adlıdırsa alıcı "Leyla sizə ısmarladı 💚", anonimdirsə (və ya hovuzun pulu ianədən deyil — məs. kampaniya qalığı)
/// "Bir xeyirxah insan sizə ısmarladı 💚" görür. Təhvildə məbləğ ianələrdən FIFO ilə xərclənir (<see cref="IPoolDonationRepository.ConsumeAsync"/>),
/// ona görə hər ianəçinin adı onun pulunun payına düşən claim-lərdə görünür. Alıcıya ianəçinin adından başqa heç nə (email, ID, məbləğ) verilmir.
/// </summary>
public sealed class PoolAttribution(IPoolDonationRepository donations, ICampaignRepository campaigns)
{
    public static string AnonymousText => $"{DonorTexts.Anonymous} sizə ısmarladı 💚";

    public static string NamedText(string name) => $"{name} sizə ısmarladı 💚";

    public async Task<PoolDonor> ForReservationAsync(Pool pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var funding = await donations.FindOldestUnusedAsync(pool.Id, cancellationToken).ConfigureAwait(false);
        var corporateId = funding?.CorporateId;
        if (corporateId is null && pool.CampaignId is { } campaignId)
        {
            var campaign = await campaigns.GetAsync(CampaignId.From(campaignId), cancellationToken).ConfigureAwait(false);
            corporateId = campaign?.CorporateId;
        }

        if (funding is null)
        {
            return new PoolDonor(AnonymousText, null, null, corporateId);
        }

        return new PoolDonor(funding.IsNamed ? NamedText(funding.DonorName!) : AnonymousText, funding.Id.Value, funding.DonorUserId, corporateId);
    }
}
