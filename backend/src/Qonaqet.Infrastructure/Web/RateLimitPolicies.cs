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

    /// <summary>Claim yaratma və kodların göstərilməsi: istifadəçi (yoxdursa IP) başına dəqiqədə 20.</summary>
    public const string Claim = "claim";

    /// <summary>Kod yoxlama/təhvil (biznes): istifadəçi başına dəqiqədə 60 — qısa kodun təxmin edilməsinə qarşı.</summary>
    public const string Redeem = "redeem";

    /// <summary>Ödəniş statusu (tətbiq sorğulayır): istifadəçi (yoxdursa IP) başına dəqiqədə 60.</summary>
    public const string Payment = "payment";

    /// <summary>Provayder callback-i (anonim, imzasız): IP başına dəqiqədə 120.</summary>
    public const string PaymentCallback = "payment-callback";

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
            o.AddValkeyFixedWindowPolicy(Claim, UserOrIp, 20, TimeSpan.FromMinutes(1));
            o.AddValkeyFixedWindowPolicy(Redeem, UserOrIp, 60, TimeSpan.FromMinutes(1));
            o.AddValkeyFixedWindowPolicy(Payment, UserOrIp, 60, TimeSpan.FromMinutes(1));
            o.AddValkeyFixedWindowPolicy(PaymentCallback, ClientIp, 120, TimeSpan.FromMinutes(1));
        });
    }

    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirst("sub")?.Value is { Length: > 0 } sub ? $"u:{sub}" : $"ip:{ClientIp(context)}";

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
