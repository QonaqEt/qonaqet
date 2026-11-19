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

namespace Qonaqet.Application.Features.Admin.Commands.SuspendBusiness;

/// <summary>Admin biznesi dayandırır: ictimai siyahılardan çıxır, yeni əməliyyat qəbul etmir. Səbəb audit log-a düşür.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record SuspendBusinessCommand(BusinessId BusinessId, string Reason) : ICommand<BusinessDto>;

public sealed class SuspendBusinessValidator : Validator<SuspendBusinessCommand>
{
    public SuspendBusinessValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(500);
    }
}

public sealed class SuspendBusinessHandler(IBusinessRepository businesses, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<SuspendBusinessCommand, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(SuspendBusinessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        var now = clock.UtcNow;
        var before = business.Status;
        if (!business.Suspend(request.Reason, now, currentUser.ActorId))
        {
            return business.ToDto();
        }

        await businesses.UpdateAsync(business, cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, string?> { ["before"] = before.ToString(), ["after"] = business.Status.ToString(), ["reason"] = request.Reason.Trim() };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.Suspend, nameof(Business), business.Id.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return business.ToDto();
    }
}
