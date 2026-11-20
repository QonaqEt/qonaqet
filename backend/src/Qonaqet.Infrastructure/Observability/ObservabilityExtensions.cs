using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>
/// Logging (JSON console + maskalama), OpenTelemetry (trace və metric → OTLP) və health check-lər.
/// Instrumentasiya paketləri yoxdur: ASP.NET Core, HttpClient, Npgsql və Mongo-nun öz
/// <see cref="ActivitySource"/>/<c>Meter</c>-ləri dinlənir.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Öz kodumuzun trace mənbəyi (outbox relay, consumer-lər, job-lar).</summary>
    public static readonly ActivitySource Source = new("Qonaqet");

    private static readonly string[] Sources =
    [
        "Qonaqet",
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Npgsql",
        "MongoDB.Driver.Core.Extensions.DiagnosticSources",
        "MongoDB.Driver",
    ];

    private static readonly string[] Meters =
    [
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Diagnostics",
        "Microsoft.AspNetCore.RateLimiting",
        "System.Net.Http",
        "System.Runtime",
        "Npgsql",
    ];

    public static IHostApplicationBuilder AddQonaqetObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.FormatterName = RedactingJsonConsoleFormatter.FormatterName);
        builder.Logging.AddConsoleFormatter<RedactingJsonConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
        builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        // OTLP yalnız endpoint verildikdə (compose-da Aspire Dashboard); əks halda heç nə göndərilmir.
        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "qonaqet"))
            .WithTracing(t => t.AddSource(Sources).AddProcessor(new SensitivePathRedactor()).SetErrorStatusOnException())
            .WithMetrics(m => m.AddMeter(Meters));
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            // Yalnız trace və metric; loglar maskalanmış JSON console-dadır (OTLP-yə xam log getmir).
            telemetry.UseOtlpExporter();
        }

        builder.Services.AddStoreHealthChecks();
        return builder;
    }
}
