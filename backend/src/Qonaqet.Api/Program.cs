using Qonaqet.Infrastructure;

namespace Qonaqet.Api;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();

        app.MapControllers();

        app.Run();
    }
}
