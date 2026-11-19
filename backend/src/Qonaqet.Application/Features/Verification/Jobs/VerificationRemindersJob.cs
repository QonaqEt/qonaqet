using System.Globalization;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Users.Common;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Verification.Jobs;

/// <summary>
/// Etibarlılıq xatırlatmaları (Worker, saatda bir yoxlayır): müddətinin bitməsinə ≤14 gün qalan hər təsdiq üçün bir dəfə push +
/// email "Statusunuzu yeniləyin". Hər xatırlatma əvvəlcə <c>reminderSentFor = validUntil</c> ilə atomik qeyd olunur, ona görə eyni dövr
/// üçün (iki instans eyni anda işləsə də) təkrar göndərilmir; admin müddəti dəyişəndə yeni dövr üçün yenidən xatırladılır.
/// Müddəti bitmiş təsdiq xatırladılmır: o, məntiqi olaraq artıq qüvvədə deyil (<c>validUntil</c> indiki anla müqayisə olunur).
/// </summary>
public sealed class VerificationRemindersJob(
    IVerificationReminderStore store,
    IUserRepository users,
    ICategoryRepository categories,
    UserPushNotifier push,
    IEmailTemplateRenderer templates,
    IEmailQueue emails,
    IClock clock) : IScheduledJob
{
    public const string Name = "verification-reminders";
    public const string EmailTemplate = "verification-renewal";

    /// <summary>Bir keçiddə ən çox bu qədər istifadəçi (qalanları növbəti keçiddə).</summary>
    public const int BatchSize = 500;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await store.FindDueAsync(now, now.Add(VerificationPolicy.RenewalWindow), BatchSize, cancellationToken).ConfigureAwait(false);
        var sent = 0;
        foreach (var reminder in due)
        {
            if (!await store.TryMarkSentAsync(reminder, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var user = await users.GetAsync(UserId.From(reminder.UserId), cancellationToken).ConfigureAwait(false);
            var category = await categories.GetAsync(CategoryId.From(reminder.CategoryId), cancellationToken).ConfigureAwait(false);
            if (user is null || user.IsBlocked || category is null)
            {
                continue;
            }

            var until = BakuTime.ToBakuDate(reminder.ValidUntil.AddTicks(-1)).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            await push.NotifyAsync(
                user.Id, NotificationType.Verification,
                new PushMessage("Statusunuzu yeniləyin", $"\"{category.Name}\" statusunuz {until} tarixində bitir."), cancellationToken).ConfigureAwait(false);

            if (user.AllowsNotification(NotificationType.Verification, NotificationChannel.Email))
            {
                var email = templates.Render(EmailTemplate, new Dictionary<string, string?>
                {
                    ["firstName"] = user.FirstName ?? "Dostumuz",
                    ["categoryName"] = category.Name,
                    ["validUntil"] = until,
                });
                await emails.EnqueueAsync(email.To(user.Email), EmailTemplate, cancellationToken).ConfigureAwait(false);
            }

            sent++;
        }

        return sent;
    }
}
