using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Commands.RestoreBusiness;

/// <summary>Admin silinmiş biznesi bərpa edir: kaskadla silinən kataloq və üzvlüklər də qayıdır (ayrıca silinənlər qalır).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RestoreBusinessCommand(BusinessId BusinessId) : ICommand;

public sealed class RestoreBusinessHandler(
    IBusinessRepository businesses,
    ICatalogItemRepository catalog,
    IBusinessMembershipRepository memberships,
    BusinessAccessSync sync,
    ICurrentUser currentUser,
    IOutbox outbox,
    IClock clock) : ICommandHandler<RestoreBusinessCommand>
{
    public async Task<Result> Handle(RestoreBusinessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await businesses.RestoreAsync(request.BusinessId, currentUser.ActorId, cancellationToken).ConfigureAwait(false))
        {
            return BusinessErrors.NotFound;
        }

        await catalog.CascadeRestoreForBusinessAsync(request.BusinessId, currentUser.ActorId, cancellationToken).ConfigureAwait(false);
        await memberships.CascadeRestoreForBusinessAsync(request.BusinessId, currentUser.ActorId, cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var members = await memberships.ListForBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        await sync.RecomputeManyAsync(members.Select(m => m.UserId), now, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync([new BusinessRestored(request.BusinessId, now)], cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
