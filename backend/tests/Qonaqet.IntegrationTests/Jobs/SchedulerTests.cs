using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Infrastructure.Jobs;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Persistence.Postgres;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Jobs;

/// <summary>Öz scheduler-imiz: advisory lock ilə tək-icraçı işlər və <c>job_runs</c> tarixçəsi (E01-T11).</summary>
[Collection(PostgresCollection.Name)]
public sealed class SchedulerTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_instances_running_a_single_runner_job_at_the_same_time_execute_it_once()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        var counter = new Counter();
        await using var instanceA = await BuildAsync(connection, counter);
        await using var instanceB = await BuildAsync(connection, counter);
        var job = instanceA.GetServices<JobDefinition>().Single(j => j.Name == "slow");

        var results = await Task.WhenAll(
            instanceA.GetRequiredService<JobRunner>().RunAsync(job, Ct),
            instanceB.GetRequiredService<JobRunner>().RunAsync(job, Ct));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, counter.Value);
        var runs = await ReadRunsAsync(instanceA);
        var run = Assert.Single(runs);
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.ItemsProcessed);
        Assert.NotNull(run.FinishedAt);

        // Kilid buraxılıb: növbəti icra yenə işləyir.
        Assert.True(await instanceB.GetRequiredService<JobRunner>().RunAsync(job, Ct));
        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public async Task A_failing_job_is_recorded_with_its_error_and_does_not_stop_the_runner()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        await using var provider = await BuildAsync(connection, new Counter());
        var job = provider.GetServices<JobDefinition>().Single(j => j.Name == "broken");

        Assert.True(await provider.GetRequiredService<JobRunner>().RunAsync(job, Ct));

        var run = Assert.Single(await ReadRunsAsync(provider));
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Contains("partlamış", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_keys_are_stable_and_distinct()
    {
        var a = new JobDefinition("gift-delivery", TimeSpan.FromMinutes(1), true, typeof(SlowJob));
        var again = new JobDefinition("gift-delivery", TimeSpan.FromMinutes(5), true, typeof(SlowJob));
        var b = new JobDefinition("claim-expiry", TimeSpan.FromMinutes(1), true, typeof(SlowJob));

        Assert.Equal(a.LockKey, again.LockKey);
        Assert.NotEqual(a.LockKey, b.LockKey);
    }

    private static async Task<ServiceProvider> BuildAsync(string connection, Counter counter)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connection,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton(counter);
        services.AddPostgres(config);
        services.AddJobScheduler();
        services.AddScheduledJob<SlowJob>("slow", TimeSpan.FromMinutes(1));
        services.AddScheduledJob<BrokenJob>("broken", TimeSpan.FromMinutes(1));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Database.MigrateAsync(Ct);
        return provider;
    }

    private static async Task<List<Infrastructure.Persistence.Postgres.Records.JobRunRecord>> ReadRunsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().JobRuns.AsNoTracking().ToListAsync(Ct);
    }

    public sealed class Counter
    {
        private int _value;

        public int Value => _value;

        public void Increment() => Interlocked.Increment(ref _value);
    }

    private sealed class SlowJob(Counter counter) : IScheduledJob
    {
        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            counter.Increment();
            await Task.Delay(1000, cancellationToken);
            return 1;
        }
    }

    private sealed class BrokenJob : IScheduledJob
    {
        public Task<int> RunAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("partlamış iş");
    }
}
