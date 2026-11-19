using Qonaqet.Domain.Users;

namespace Qonaqet.Domain.Tests.Users;

public sealed class NotificationPreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Everything_is_enabled_by_default()
    {
        var user = User.Register("a@example.az", Now);

        foreach (var type in Enum.GetValues<NotificationType>())
        {
            Assert.True(user.AllowsNotification(type, NotificationChannel.Push));
            Assert.True(user.AllowsNotification(type, NotificationChannel.Email));
        }
    }

    [Fact]
    public void A_type_can_be_disabled_per_channel_and_enabled_again()
    {
        var user = User.Register("a@example.az", Now);

        user.SetNotificationPreference(NotificationType.Campaigns, NotificationChannel.Push, enabled: false, Now);
        user.SetNotificationPreference(NotificationType.Campaigns, NotificationChannel.Push, enabled: false, Now);

        Assert.False(user.AllowsNotification(NotificationType.Campaigns, NotificationChannel.Push));
        Assert.True(user.AllowsNotification(NotificationType.Campaigns, NotificationChannel.Email));
        Assert.Single(user.NotificationOptOuts);

        user.SetNotificationPreference(NotificationType.Campaigns, NotificationChannel.Push, enabled: true, Now);
        Assert.Empty(user.NotificationOptOuts);
    }
}
