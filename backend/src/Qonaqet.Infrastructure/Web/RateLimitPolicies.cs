using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Infrastructure.Caching;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// Endpoint rate limit siyasətləri (Valkey üzərində, bütün instanslar üçün ümumi). Controller-də
/// <c>[EnableRateLimiting(RateLimitPolicies.Otp)]</c> ilə tətbiq olunur. Partition: klient IP-si.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>OTP göndər/yoxla: IP başına dəqiqədə 10.</summary>
    public const string Otp = "otp";

    /// <summary>Token yeniləmə və çıxış: IP başına dəqiqədə 30.</summary>
    public const string Auth = "auth";

    public static readonly Error RateLimited = Error.TooManyRequests("rate_limited", "Çox sayda sorğu. Bir az sonra yenidən cəhd edin.");

    public static IServiceCollection AddQonaqetRateLimits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(context.HttpContext.Response.Body, RateLimited.ToProblemDetails(), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            };
            o.AddValkeyFixedWindowPolicy(Otp, ClientIp, 10, TimeSpan.FromMinutes(1));
            o.AddValkeyFixedWindowPolicy(Auth, ClientIp, 30, TimeSpan.FromMinutes(1));
        });
    }

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
