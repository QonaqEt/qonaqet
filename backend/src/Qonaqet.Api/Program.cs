using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Api;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddQonaqetWeb("v1", "QONAQET API v1");
        builder.AddQonaqetObservability("qonaqet-api");
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();

        app.UseQonaqetWeb();

        app.Run();
    }
}
