using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Hovuz claim-ində alıcıya göstərilən ianəçi mətni və atribusiya olunan ianə.</summary>
public sealed record PoolDonor(string Text, Guid? DonationId);

/// <summary>
/// Hovuz claim-inin donor atribusiyası (proporsional): claim hovuzun <b>ən köhnə istifadə olunmamış</b> təsdiqlənmiş ianəsindən maliyyələşir.
/// Həmin ianə adlıdırsa alıcı "Leyla sizə ısmarladı 💚", anonimdirsə (və ya hovuzun pulu ianədən deyil — məs. kampaniya qalığı)
/// "Bir xeyirxah insan sizə ısmarladı 💚" görür. Təhvildə məbləğ ianələrdən FIFO ilə xərclənir (<see cref="IPoolDonationRepository.ConsumeAsync"/>),
/// ona görə hər ianəçinin adı onun pulunun payına düşən claim-lərdə görünür. Alıcıya ianəçinin adından başqa heç nə (email, ID, məbləğ) verilmir.
/// </summary>
public sealed class PoolAttribution(IPoolDonationRepository donations)
{
    public static string AnonymousText => $"{DonorTexts.Anonymous} sizə ısmarladı 💚";

    public static string NamedText(string name) => $"{name} sizə ısmarladı 💚";

    public async Task<PoolDonor> ForReservationAsync(PoolId poolId, CancellationToken cancellationToken)
    {
        var funding = await donations.FindOldestUnusedAsync(poolId, cancellationToken).ConfigureAwait(false);
        if (funding is null)
        {
            return new PoolDonor(AnonymousText, null);
        }

        return new PoolDonor(funding.IsNamed ? NamedText(funding.DonorName!) : AnonymousText, funding.Id.Value);
    }
}
