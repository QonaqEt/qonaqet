using System.Globalization;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>
/// Konteyner healthcheck-i üçün probe rejimi (<c>host --health-probe</c>): chiseled image-də curl yoxdur.
/// HTTP host-ları öz <c>/health/live</c>-ını yoxlayır; Worker HTTP açmır, onun canlılığı
/// <see cref="HeartbeatFile"/>-ın son yenilənmə vaxtına görə təyin olunur.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--health-probe";

    public static readonly string HeartbeatFile = Path.Combine(Path.GetTempPath(), "qonaqet-worker.alive");

    public static readonly TimeSpan HeartbeatMaxAge = TimeSpan.FromSeconds(60);

    public static bool IsRequested(string[] args) => args is { Length: > 0 } && args[0] == Argument;

    /// <summary>HTTP host probe-u: 0 = sağlam, 1 = yox.</summary>
    public static int RunHttp(string? portVariable = null)
    {
        var ports = Environment.GetEnvironmentVariable(portVariable ?? "ASPNETCORE_HTTP_PORTS") ?? "8080";
        var port = ports.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)[0];
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live")).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>Worker probe-u: heartbeat faylı son <see cref="HeartbeatMaxAge"/> ərzində yenilənibsə 0.</summary>
    public static int RunHeartbeat(DateTimeOffset now)
    {
        if (!File.Exists(HeartbeatFile))
        {
            return 1;
        }

        var text = File.ReadAllText(HeartbeatFile);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var beat) && now - beat <= HeartbeatMaxAge
            ? 0
            : 1;
    }
}
