using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Hovuz claim-ində alıcıya göstərilən ianəçi mətni və atribusiya olunan ianə.</summary>
public sealed record PoolDonor(string Text, Guid? DonationId);

/// <summary>
/// Hovuz claim-inin donor atribusiyası. Hələlik hər hovuz claim-i anonim göstərilir ("Bir xeyirxah insan sizə ısmarladı 💚"); adlı ianələrin
/// proporsional atribusiyası E08-T04-dədir.
/// </summary>
public sealed class PoolAttribution
{
    public static string AnonymousText => $"{DonorTexts.Anonymous} sizə ısmarladı 💚";

    public Task<PoolDonor> ForReservationAsync(PoolId poolId, CancellationToken cancellationToken) =>
        Task.FromResult(new PoolDonor(AnonymousText, null));
}
