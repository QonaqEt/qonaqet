namespace Qonaqet.Application.Abstractions.Jobs;

/// <summary>
/// Worker-in öz scheduler-i ilə dövri işləyən iş (gift delivery, claim expiry, payment re-check …).
/// Hər icra ayrıca DI scope-da olur. İş öz elementlərini <b>atomik status keçidi</b> ilə götürməlidir ki,
/// eyni anda iki instans işləsə belə heç nə iki dəfə emal olunmasın; <c>SingleRunner</c> işlər əlavə olaraq
/// Postgres advisory lock ilə qorunur.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Bir keçid; emal olunan elementlərin sayını qaytarır (<c>job_runs.items_processed</c>).</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}
