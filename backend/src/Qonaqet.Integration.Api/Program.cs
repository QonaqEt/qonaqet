using Microsoft.AspNetCore.Mvc;
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

        builder.Services.AddQonaqetWeb(PartnerOpenApi.DocumentName, "QONAQET Partner API v1");
        builder.Services.AddPartnerOpenApiDetails();
        builder.AddQonaqetObservability("qonaqet-integration-api");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddQonaqetPartnerAuthentication();
        builder.Services.AddScoped<PartnerIdempotencyFilter>();
        builder.Services.Configure<MvcOptions>(o => o.Filters.AddService<PartnerIdempotencyFilter>());

        var app = builder.Build();

        app.UseQonaqetWeb();
        app.UseMiddleware<PartnerAuditMiddleware>();

        app.Run();
        return 0;
    }
}
