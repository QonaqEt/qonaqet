using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerReverse;

/// <summary>
/// <c>POST /partner/v1/redemptions/{id}/reverse</c> (<c>redemptions:write</c>): client öz təhvilini 15 dəqiqə ərzində geri alır — qalıq bərpa
/// olunur, ledger və xal tərs yazılarla düzəlir (<see cref="RedemptionReversal"/>), <c>redemption.reversed</c> webhook-u hadisədən göndərilir.
/// Başqa client-in və ya işçinin təhvili "tapılmadı" sayılır.
/// </summary>
[RequiresRole(PartnerScopes.RedemptionsWrite)]
public sealed record PartnerReverseRedemptionCommand(Guid RedemptionId) : ICommand<ReversedRedemption>, INonTransactionalCommand;

public sealed class PartnerReverseRedemptionHandler(PartnerAccess access, RedemptionReversal reversal)
    : ICommandHandler<PartnerReverseRedemptionCommand, ReversedRedemption>
{
    public async Task<Result<ReversedRedemption>> Handle(PartnerReverseRedemptionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        return await reversal.ReverseAsync(request.RedemptionId, client.Value.Id.Value, client.Value.CanAccessBusiness, cancellationToken)
            .ConfigureAwait(false);
    }
}
