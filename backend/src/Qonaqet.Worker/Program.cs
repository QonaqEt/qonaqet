using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Messaging;

namespace Qonaqet.Worker;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddMessagingHost(builder.Configuration);

        // Scheduler E01-T11 ilə əlavə olunur.
        builder.Build().Run();
    }
}
