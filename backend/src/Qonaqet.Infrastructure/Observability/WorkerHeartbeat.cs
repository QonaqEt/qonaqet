using System.Globalization;
using Microsoft.Extensions.Hosting;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>Worker-in canlılıq siqnalı: hər 10 saniyədən bir heartbeat faylına vaxt yazır.</summary>
public sealed class WorkerHeartbeat(IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            await File.WriteAllTextAsync(HealthProbe.HeartbeatFile, clock.UtcNow.ToString("O", CultureInfo.InvariantCulture), stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
