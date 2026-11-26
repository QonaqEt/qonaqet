using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Integration.Api;

public sealed class Program
{
    public static int Main(string[] args)
    {
        if (HealthProbe.IsRequested(args))
        {
            return HealthProbe.RunHttp();
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddQonaqetWeb("partner-v1", "QONAQET Partner API v1");
        builder.AddQonaqetObservability("qonaqet-integration-api");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddQonaqetPartnerAuthentication();

        var app = builder.Build();

        app.UseQonaqetWeb();

        app.Run();
        return 0;
    }
}
