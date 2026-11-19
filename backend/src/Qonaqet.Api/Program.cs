using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Api;

public sealed class Program
{
    public static int Main(string[] args)
    {
        if (HealthProbe.IsRequested(args))
        {
            return HealthProbe.RunHttp();
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddQonaqetWeb("v1", "QONAQET API v1");
        builder.Services.AddQonaqetUserAuthentication();
        builder.AddQonaqetObservability("qonaqet-api");
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();

        app.UseQonaqetWeb();

        app.Run();
        return 0;
    }
}
