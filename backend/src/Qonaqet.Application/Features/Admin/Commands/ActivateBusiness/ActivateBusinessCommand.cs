using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Commands.ActivateBusiness;

/// <summary>
/// Admin biznesi aktivləşdirir (<c>Pending</c>/<c>Suspended</c> → <c>Active</c>). İlk aktivləşdirmədə müraciətçi
/// <c>BusinessOwner</c> rolunu alır (yeni token-də görünür). Biznes və istifadəçi dəyişikliyi bir Mongo tranzaksiyasındadır.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ActivateBusinessCommand(BusinessId BusinessId) : ICommand<BusinessDto>;

public sealed class ActivateBusinessHandler(
    IBusinessRepository businesses, IUserRepository users, BusinessAccessSync access, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<ActivateBusinessCommand, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(ActivateBusinessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        var owner = await users.GetAsync(UserId.From(business.OwnerUserId), cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return BusinessErrors.OwnerNotFound;
        }

        var now = clock.UtcNow;
        var before = business.Status;
        if (!business.Activate(now, currentUser.ActorId))
        {
            return business.ToDto();
        }

        await businesses.UpdateAsync(business, cancellationToken).ConfigureAwait(false);
        await access.EnsureOwnerAsync(business, owner, now, cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, string?> { ["before"] = before.ToString(), ["after"] = business.Status.ToString() };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.Activate, nameof(Business), business.Id.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return business.ToDto();
    }
}
