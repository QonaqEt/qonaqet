using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Commands.CancelClaim;

/// <summary>
/// <c>POST claims/{id}/cancel</c>: alıcı öz rezervindən imtina edir, vahid dərhal təklifə qayıdır (başqası ala bilər). Yalnız
/// <c>Reserved</c> claim; təhvil verilmiş/bitmiş claim dəyişmir. Atomik şərtli keçid, tranzaksiyasız (<see cref="ClaimRelease"/>).
/// </summary>
public sealed record CancelClaimCommand(ClaimId ClaimId) : ICommand, INonTransactionalCommand;

public sealed class CancelClaimHandler(IClaimRepository claims, ClaimRelease release, ICurrentUser currentUser, IClock clock) : ICommandHandler<CancelClaimCommand>
{
    public const string Reason = "recipient_cancelled";

    public async Task<Result> Handle(CancelClaimCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var claim = await claims.GetAsync(request.ClaimId, cancellationToken).ConfigureAwait(false);
        if (claim is null || claim.RecipientUserId != currentUser.ActorId)
        {
            return SocialErrors.ClaimNotFound;
        }

        if (!claim.IsReserved)
        {
            return RedemptionCodeResolver.NotRedeemable(claim, clock.UtcNow);
        }

        return await release.CancelAsync(claim, Reason, currentUser.ActorId, cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : SocialErrors.AlreadyRedeemed;
    }
}
