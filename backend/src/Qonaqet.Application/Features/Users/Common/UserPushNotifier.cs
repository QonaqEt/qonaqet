using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Features.Users.Common;

/// <summary>
/// İstifadəçinin bütün cihazlarına push göndərir və provayderin etibarsız saydığı token-ləri dərhal təmizləyir
/// (növbəti göndərmələr ölü cihazlara getməsin). Hədiyyə/claim bildirişləri bunu işlədəcək.
/// </summary>
public sealed class UserPushNotifier(IUserRepository users, IPushSender pushSender)
{
    public async Task NotifyAsync(UserId userId, PushMessage message, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.FcmTokens.Count == 0)
        {
            return;
        }

        var result = await pushSender.SendAsync([.. user.FcmTokens], message, cancellationToken).ConfigureAwait(false);
        if (result.InvalidTokens.Count > 0)
        {
            await users.RemoveFcmTokensAsync(userId, [.. result.InvalidTokens], cancellationToken).ConfigureAwait(false);
        }
    }
}
