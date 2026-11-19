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

namespace Qonaqet.Application.Features.Businesses.Commands.AddStaff;

/// <summary>
/// Sahib email ilə işçi əlavə edir (<c>POST business/{businessId}/staff</c>). Email-in QONAQET hesabı olmalıdır; işçi
/// <c>BusinessStaff</c> rolunu alır və ona bildiriş emaili gedir. Üzvlük, rol və audit bir tranzaksiyadadır.
/// </summary>
[RequiresActiveAccount]
public sealed record AddStaffCommand(BusinessId BusinessId, string Email) : ICommand<StaffMemberDto>;

public sealed class AddStaffValidator : Validator<AddStaffCommand>
{
    public AddStaffValidator()
    {
        RuleFor<string?>(x => x.Email).NotEmpty().MaxLength(EmailAddress.MaxLength).Email();
    }
}

public sealed class AddStaffHandler(
    BusinessAccess access,
    BusinessAccessSync sync,
    IUserRepository users,
    IBusinessMembershipRepository memberships,
    IEmailTemplateRenderer templates,
    IEmailQueue emails,
    ICurrentUser currentUser,
    IOutbox outbox,
    IClock clock) : ICommandHandler<AddStaffCommand, StaffMemberDto>
{
    public const string EmailTemplate = "staff-added";

    public async Task<Result<StaffMemberDto>> Handle(AddStaffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var staff = await users.GetByEmailAsync(request.Email, cancellationToken).ConfigureAwait(false);
        if (staff is null || staff.IsBlocked)
        {
            return BusinessErrors.StaffAccountNotFound;
        }

        if (await memberships.FindAsync(request.BusinessId, staff.Id.Value, cancellationToken).ConfigureAwait(false) is not null)
        {
            return BusinessErrors.AlreadyMember;
        }

        var now = clock.UtcNow;
        var membership = BusinessMembership.Create(request.BusinessId, staff.Id.Value, MembershipRole.Staff, now, currentUser.ActorId);
        await memberships.AddAsync(membership, cancellationToken).ConfigureAwait(false);
        await sync.RecomputeAsync(staff, now, cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, string?> { ["userId"] = staff.Id.Value.ToString("D"), ["role"] = nameof(MembershipRole.Staff) };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.AddMember, nameof(Business), request.BusinessId.Value, data)],
            cancellationToken).ConfigureAwait(false);

        var email = templates.Render(EmailTemplate, new Dictionary<string, string?> { ["businessName"] = grant.Value.Business.Name });
        await emails.EnqueueAsync(email.To(staff.Email), EmailTemplate, cancellationToken).ConfigureAwait(false);
        return StaffMemberDto.From(membership, staff);
    }
}
