using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Users.Queries.GetNotificationPreferences;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Users.Commands.UpdateNotificationPreferences;

/// <summary>
/// <c>PATCH me/notifications</c>: göndərilən növlərin push/email seçimlərini dəyişir, göndərilməyənlərə toxunmur.
/// Cavab — bütün növlərin yenilənmiş vəziyyəti.
/// </summary>
public sealed record UpdateNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceItem> Items) : ICommand<NotificationPreferencesDto>;

public sealed class UpdateNotificationPreferencesValidator : Validator<UpdateNotificationPreferencesCommand>
{
    public UpdateNotificationPreferencesValidator()
    {
        RuleFor<IReadOnlyList<NotificationPreferenceItem>?>(x => x.Items)
            .Must(items => items is { Count: > 0 }, "notification_preferences_empty", "Ən azı bir bildiriş növü göndərilməlidir.")
            .Must(items => items is null || items.All(i => Enum.IsDefined(i.Type)), "notification_type_unknown", "Naməlum bildiriş növü.")
            .Must(items => items is null || items.Select(i => i.Type).Distinct().Count() == items.Count, "notification_type_duplicate", "Bildiriş növü təkrarlanır.");
    }
}

public sealed class UpdateNotificationPreferencesHandler(IUserRepository users, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesDto>
{
    public async Task<Result<NotificationPreferencesDto>> Handle(UpdateNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(UserId.From(currentUser.ActorId!.Value), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        var now = clock.UtcNow;
        foreach (var item in request.Items)
        {
            user.SetNotificationPreference(item.Type, NotificationChannel.Push, item.Push, now);
            user.SetNotificationPreference(item.Type, NotificationChannel.Email, item.Email, now);
        }

        await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        return user.ToNotificationPreferencesDto();
    }
}
