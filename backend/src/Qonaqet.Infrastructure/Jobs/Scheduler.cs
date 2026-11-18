using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Jobs;

/// <summary>Qeydiyyatdan keçmiş iş: ad (sabit, <c>job_runs.job_name</c>), interval və tək-icraçı olub-olmaması.</summary>
public sealed record JobDefinition(string Name, TimeSpan Interval, bool SingleRunner, Type JobType)
{
    /// <summary>Advisory lock açarı: adın sabit 64-bit FNV-1a hash-i (<see cref="string.GetHashCode()"/> prosesdən prosesə dəyişir).</summary>
    public long LockKey { get; } = Fnv1a(Name);

    private static long Fnv1a(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes("qonaqet.job:" + value))
        {
            hash ^= b;
            hash *= prime;
        }

        return unchecked((long)hash);
    }
}

public static class SchedulerRegistration
{
    /// <summary>İşi scheduler-ə əlavə edir. İş tipi scoped qeyd olunur və hər icrada yenidən yaradılır.</summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services, string name, TimeSpan interval, bool singleRunner = true)
        where TJob : class, IScheduledJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        services.AddScoped<TJob>();
        services.AddSingleton(new JobDefinition(name, interval, singleRunner, typeof(TJob)));
        return services;
    }

    /// <summary>Scheduler host xidməti (yalnız Worker).</summary>
    public static IServiceCollection AddJobScheduler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<JobRunner>();
        services.AddHostedService<JobScheduler>();
        return services;
    }
}

/// <summary><c>job_runs</c> nəticə statusları.</summary>
public static class JobRunStatus
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

/// <summary>
/// Bir işin bir icrası: (tək-icraçı işlər üçün) <c>pg_try_advisory_lock</c>, <c>job_runs</c> sətri, iş, nəticə.
/// Kilid ayrıca Npgsql bağlantısında saxlanır və icra bitəndə (və ya bağlantı qırılanda) açılır.
/// </summary>
public sealed partial class JobRunner(IServiceScopeFactory scopes, IClock clock, ILogger<JobRunner> logger)
{
    /// <summary>İcra olundusa <c>true</c>; kilidi başqa instans tutubsa <c>false</c>.</summary>
    public async Task<bool> RunAsync(JobDefinition job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();

        NpgsqlConnection? lockConnection = null;
        var locked = false;
        try
        {
            if (job.SingleRunner)
            {
                lockConnection = new NpgsqlConnection(context.Database.GetConnectionString());
                await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                locked = await ExecuteLockAsync(lockConnection, "SELECT pg_try_advisory_lock(@key)", job.LockKey, cancellationToken).ConfigureAwait(false);
                if (!locked)
                {
                    return false;
                }
            }

            var run = new JobRunRecord
            {
                Id = Guid.CreateVersion7(),
                JobName = job.Name,
                Instance = Environment.MachineName,
                Status = JobRunStatus.Running,
                StartedAt = clock.UtcNow,
            };
            context.JobRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var instance = (IScheduledJob)scope.ServiceProvider.GetRequiredService(job.JobType);
                run.ItemsProcessed = await instance.RunAsync(cancellationToken).ConfigureAwait(false);
                run.Status = JobRunStatus.Succeeded;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                run.Status = JobRunStatus.Failed;
                run.Error = ex.GetType().Name + ": " + (ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message);
                LogJobFailed(ex, job.Name);
            }

            run.FinishedAt = clock.UtcNow;
            context.ChangeTracker.Clear();
            context.JobRuns.Update(run);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        finally
        {
            if (lockConnection is not null)
            {
                // Bağlantı pool-a qayıdır (fiziki bağlanmır), ona görə kilid açıq buraxılmalıdır.
                // Proses çöksə, bağlantı qırılır və Postgres kilidi özü buraxır.
                if (locked)
                {
                    await ExecuteLockAsync(lockConnection, "SELECT pg_advisory_unlock(@key)", job.LockKey, CancellationToken.None).ConfigureAwait(false);
                }

                await lockConnection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> ExecuteLockAsync(NpgsqlConnection connection, string sql, long key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "İş {Job} uğursuz oldu")]
    private partial void LogJobFailed(Exception exception, string job);
}

/// <summary><c>BackgroundService</c> + hər iş üçün öz <see cref="PeriodicTimer"/>-i.</summary>
public sealed partial class JobScheduler(IEnumerable<JobDefinition> jobs, JobRunner runner, ILogger<JobScheduler> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(jobs.Select(job => LoopAsync(job, stoppingToken)));

    private async Task LoopAsync(JobDefinition job, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(job.Interval);
        do
        {
            try
            {
                await runner.RunAsync(job, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSchedulerError(ex, job.Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduler {Job} işini başlada bilmədi")]
    private partial void LogSchedulerError(Exception exception, string job);
}
