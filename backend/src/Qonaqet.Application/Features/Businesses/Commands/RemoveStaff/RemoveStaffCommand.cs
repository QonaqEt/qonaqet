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

namespace Qonaqet.Application.Features.Businesses.Commands.RemoveStaff;

/// <summary>
/// Sahib işçini çıxarır (<c>DELETE business/{businessId}/staff/{userId}</c>): üzvlük soft delete olunur (audit ilə),
/// başqa biznesdə işçi deyilsə <c>BusinessStaff</c> rolu götürülür. Sahib üzvlüyü bu yolla silinmir.
/// </summary>
[RequiresActiveAccount]
public sealed record RemoveStaffCommand(BusinessId BusinessId, Guid UserId, string? Reason) : ICommand;

public sealed class RemoveStaffValidator : Validator<RemoveStaffCommand>
{
    public RemoveStaffValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).MaxLength(500);
    }
}

public sealed class RemoveStaffHandler(
    BusinessAccess access, BusinessAccessSync sync, IUserRepository users, IBusinessMembershipRepository memberships, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<RemoveStaffCommand>
{
    public async Task<Result> Handle(RemoveStaffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var membership = await memberships.FindAsync(request.BusinessId, request.UserId, cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            return BusinessErrors.MemberNotFound;
        }

        if (membership.Role == MembershipRole.Owner)
        {
            return BusinessErrors.CannotRemoveOwner;
        }

        await memberships.SoftDeleteAsync(membership.Id, currentUser.ActorId, request.Reason ?? "staff removed", cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (await users.GetAsync(UserId.From(request.UserId), cancellationToken).ConfigureAwait(false) is { } staff)
        {
            await sync.RecomputeAsync(staff, now, cancellationToken).ConfigureAwait(false);
        }

        var data = new Dictionary<string, string?> { ["userId"] = request.UserId.ToString("D"), ["role"] = membership.Role.ToString() };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.RemoveMember, nameof(Business), request.BusinessId.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
