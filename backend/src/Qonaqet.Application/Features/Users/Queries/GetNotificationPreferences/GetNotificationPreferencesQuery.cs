using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Users.Queries.GetNotificationPreferences;

/// <summary><c>GET me/notifications</c>: hər bildiriş növü üçün push və email açıq/söndürülüb.</summary>
public sealed record GetNotificationPreferencesQuery : IQuery<NotificationPreferencesDto>;

public sealed record NotificationPreferenceItem(NotificationType Type, bool Push, bool Email);

public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationPreferenceItem> Items);

public static class NotificationPreferencesMapping
{
    public static NotificationPreferencesDto ToNotificationPreferencesDto(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new NotificationPreferencesDto([.. Enum.GetValues<NotificationType>().Select(type => new NotificationPreferenceItem(
            type,
            user.AllowsNotification(type, NotificationChannel.Push),
            user.AllowsNotification(type, NotificationChannel.Email)))]);
    }
}

public sealed class GetNotificationPreferencesHandler(IUserRepository users, ICurrentUser currentUser)
    : IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<Result<NotificationPreferencesDto>> Handle(GetNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        return user is null ? AuthErrors.UserNotFound : user.ToNotificationPreferencesDto();
    }
}
