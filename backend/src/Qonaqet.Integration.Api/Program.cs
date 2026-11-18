using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Integration.Api;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddQonaqetWeb("partner-v1", "QONAQET Partner API v1");
        builder.AddQonaqetObservability("qonaqet-integration-api");
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();

        app.UseQonaqetWeb();

        app.Run();
    }
}
