using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.DeleteBusiness;

/// <summary>
/// Admin biznesi soft delete edir (<c>DELETE admin/businesses/{id}</c>). Bir Mongo tranzaksiyasında: biznes, onun kataloq
/// elementləri və üzvlükləri soft delete olunur (kaskad səbəbi <see cref="BusinessCascade.DeleteReason"/>), üzvlərin biznes
/// rolları yenidən hesablanır və <see cref="BusinessDeleted"/> outbox-a yazılır — təkliflər və aktiv claim-lər onun
/// consumer-indədir (E06-T09). Fiziki silmə yoxdur; <see cref="RestoreBusinessCommand"/> hamısını geri qaytarır.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record DeleteBusinessCommand(BusinessId BusinessId, string Reason) : ICommand;

public sealed class DeleteBusinessValidator : Validator<DeleteBusinessCommand>
{
    public DeleteBusinessValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(500);
    }
}

public sealed class DeleteBusinessHandler(
    IBusinessRepository businesses,
    ICatalogItemRepository catalog,
    IBusinessMembershipRepository memberships,
    BusinessAccessSync sync,
    ICurrentUser currentUser,
    IOutbox outbox,
    IClock clock) : ICommandHandler<DeleteBusinessCommand>
{
    public async Task<Result> Handle(DeleteBusinessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var members = await memberships.ListForBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (!await businesses.SoftDeleteAsync(request.BusinessId, currentUser.ActorId, request.Reason.Trim(), cancellationToken).ConfigureAwait(false))
        {
            return BusinessErrors.NotFound;
        }

        await catalog.CascadeDeleteForBusinessAsync(request.BusinessId, currentUser.ActorId, cancellationToken).ConfigureAwait(false);
        await memberships.CascadeDeleteForBusinessAsync(request.BusinessId, currentUser.ActorId, cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        await sync.RecomputeManyAsync(members.Select(m => m.UserId), now, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync([new BusinessDeleted(request.BusinessId, now)], cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
