using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Caching;

/// <summary>
/// ASP.NET Core <c>RateLimiter</c> middleware-i üçün adapter: hər partition (məs. IP + endpoint) üçün
/// sayğac Valkey-dədir (<see cref="IRateLimitStore"/>), ona görə limit bütün instanslar üzrə ümumidir.
/// </summary>
public sealed class ValkeyWindowRateLimiter(IRateLimitStore store, string partitionKey, int permitLimit, TimeSpan window) : RateLimiter
{
    private static readonly RateLimitLease Failed = new Lease(false, null);
    private long _lastUsedTicks = DateTime.UtcNow.Ticks;

    public override TimeSpan? IdleDuration => TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastUsedTicks));

    public override RateLimiterStatistics? GetStatistics() => null;

    /// <summary>Sinxron yol Valkey-ə getmir: middleware sonra asinxron <see cref="AcquireAsyncCore"/>-u çağırır.</summary>
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Failed;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
        var decision = await store.TryAcquireAsync(partitionKey, permitLimit, window, cancellationToken).ConfigureAwait(false);
        return new Lease(decision.Allowed, decision.Allowed ? null : decision.RetryAfter);
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired { get; } = acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}

public static class ValkeyRateLimiterExtensions
{
    /// <summary>Valkey üzərində fixed-window siyasəti əlavə edir (OTP, claim, redeem, ödəniş endpoint-ləri üçün).</summary>
    public static RateLimiterOptions AddValkeyFixedWindowPolicy(
        this RateLimiterOptions options, string policyName, Func<HttpContext, string> partitionKey, int permitLimit, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(partitionKey);
        return options.AddPolicy(policyName, context => RateLimitPartition.Get(
            $"{policyName}:{partitionKey(context)}",
            key => new ValkeyWindowRateLimiter(context.RequestServices.GetRequiredService<IRateLimitStore>(), key, permitLimit, window)));
    }
}
