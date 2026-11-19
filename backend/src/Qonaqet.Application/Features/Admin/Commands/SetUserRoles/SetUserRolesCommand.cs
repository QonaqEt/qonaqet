using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.SetUserRoles;

/// <summary>
/// Admin istifadəçinin rollarını təyin edir (<c>User</c> həmişə qalır). Yeni rollar növbəti token yeniləməsindən
/// sonra JWT-də görünür. Admin öz admin rolunu silə bilməz. Dəyişiklik audit log-a düşür.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record SetUserRolesCommand(UserId UserId, IReadOnlyList<Role> Roles) : ICommand;

public sealed class SetUserRolesValidator : Validator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(x => x.Roles).Must(r => r is not null && r.All(Enum.IsDefined), "role_invalid", "Naməlum rol.");
    }
}

public sealed class SetUserRolesHandler(IUserRepository users, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<SetUserRolesCommand>
{
    public async Task<Result> Handle(SetUserRolesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        if (user.Id.Value == currentUser.ActorId && !request.Roles.Contains(Role.Admin))
        {
            return AuthErrors.CannotRemoveOwnAdmin;
        }

        var now = clock.UtcNow;
        var before = string.Join(',', user.Roles);
        user.SetRoles(request.Roles, now, currentUser.ActorId);
        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, string?> { ["before"] = before, ["after"] = string.Join(',', user.Roles) };
        await outbox.AddAsync([new AuditRecorded(now, currentUser.ActorId, "User", UserAuditActions.SetRoles, nameof(User), user.Id.Value, data)], cancellationToken)
            .ConfigureAwait(false);
        return Result.Success();
    }
}
