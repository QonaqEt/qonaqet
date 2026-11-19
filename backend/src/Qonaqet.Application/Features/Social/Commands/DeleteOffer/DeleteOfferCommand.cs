using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.DeleteOffer;

/// <summary>
/// <c>DELETE business/{businessId}/offers/{offerId}</c>: həmişə soft delete (audit log ilə). Artıq rezerv olunmuş claim-lər
/// öz müddəti ərzində təhvil verilə bilər.
/// </summary>
[RequiresActiveAccount]
public sealed record DeleteOfferCommand(BusinessId BusinessId, OfferId OfferId, string? Reason) : ICommand;

public sealed class DeleteOfferValidator : Validator<DeleteOfferCommand>
{
    public DeleteOfferValidator()
    {
        RuleFor(x => x.Reason).MaxLength(500);
    }
}

public sealed class DeleteOfferHandler(BusinessAccess access, IOfferRepository offers, ICurrentUser currentUser) : ICommandHandler<DeleteOfferCommand>
{
    public async Task<Result> Handle(DeleteOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var offer = await offers.GetAsync(request.OfferId, cancellationToken).ConfigureAwait(false);
        if (offer is null || offer.BusinessId != request.BusinessId)
        {
            return SocialErrors.OfferNotFound;
        }

        await offers.SoftDeleteAsync(offer.Id, currentUser.ActorId, request.Reason ?? "offer deleted", cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
