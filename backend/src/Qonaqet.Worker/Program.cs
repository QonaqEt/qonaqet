using Qonaqet.Infrastructure;

namespace Qonaqet.Worker;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddInfrastructure(builder.Configuration);

        // Scheduler, outbox relay və consumer-lər E01-T09 / E01-T11 ilə əlavə olunur.
        builder.Build().Run();
    }
}
