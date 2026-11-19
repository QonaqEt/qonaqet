using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Users.Queries.GetMe;

/// <summary>Cari istifadəçinin profili.</summary>
public sealed record GetMeQuery : IQuery<MeDto>;

public sealed record MeDto(
    UserId Id,
    string Email,
    string? FirstName,
    string? LastName,
    Guid? AvatarFileId,
    IReadOnlyList<Role> Roles,
    bool ProfileCompleted);

public static class MeMapping
{
    public static MeDto ToMeDto(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new MeDto(user.Id, user.Email, user.FirstName, user.LastName, user.AvatarFileId, user.Roles, user.ProfileCompleted);
    }
}

public sealed class GetMeHandler(IUserRepository users, ICurrentUser currentUser) : IQueryHandler<GetMeQuery, MeDto>
{
    public async Task<Result<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        return user is null ? AuthErrors.UserNotFound : user.ToMeDto();
    }
}
