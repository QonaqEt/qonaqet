using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Commands.SetBusinessSandbox;

/// <summary>
/// Admin biznesi sandbox (test) kimi işarələyir və ya geri qaytarır (E13-T09). Sandbox biznes yalnız sandbox inteqrasiya client-ləri və sandbox
/// hədiyyələri ilə işləyir, ictimai axtarışda və reytinqdə görünmür.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record SetBusinessSandboxCommand(BusinessId BusinessId, bool IsSandbox) : ICommand<BusinessDto>;

public sealed class SetBusinessSandboxHandler(IBusinessRepository businesses, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<SetBusinessSandboxCommand, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(SetBusinessSandboxCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        var now = clock.UtcNow;
        if (!business.SetSandbox(request.IsSandbox, now, currentUser.ActorId))
        {
            return business.ToDto();
        }

        await businesses.UpdateAsync(business, cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, string?> { ["isSandbox"] = request.IsSandbox ? "true" : "false" };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", BusinessAuditActions.SetSandbox, nameof(Business), business.Id.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return business.ToDto();
    }
}
