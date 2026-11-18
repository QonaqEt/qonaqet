using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Jobs;
using Qonaqet.Infrastructure.Messaging;

namespace Qonaqet.Worker;

public static class Program
{
    public static int Main(string[] args)
    {
        if (HealthProbe.IsRequested(args))
        {
            return HealthProbe.RunHeartbeat(DateTimeOffset.UtcNow);
        }

        var builder = Host.CreateApplicationBuilder(args);

        builder.AddQonaqetObservability("qonaqet-worker");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddMessagingHost(builder.Configuration);

        builder.Services.AddJobScheduler();

        // Konkret işlər (payment re-check, gift delivery, claim expiry …) öz epic-ləri ilə AddScheduledJob<T>() ilə gəlir.
        builder.Services.AddHostedService<WorkerHeartbeat>();
        builder.Build().Run();
        return 0;
    }
}
