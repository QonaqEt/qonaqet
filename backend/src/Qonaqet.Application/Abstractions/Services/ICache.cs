namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// Törəmə/keçici məlumat üçün keş (Valkey). <b>Heç vaxt həqiqət mənbəyi deyil</b>: keş boş və ya əlçatmaz
/// olanda oxu "miss" qaytarır, yazı səssizcə buraxılır, çağıran məlumatı əsas store-dan oxuyur.
/// </summary>
public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>Keş açarını etibarsız edir (biznes qeydi deyil, ona görə silinə bilər).</summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Keşdə varsa onu, yoxdursa <paramref name="factory"/>-ni işlədib keşə yazır.</summary>
public static class CacheExtensions
{
    public static async Task<T> GetOrSetAsync<T>(
        this ICache cache, string key, TimeSpan timeToLive, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await cache.GetAsync<CacheBox<T>>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.Value;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await cache.SetAsync(key, new CacheBox<T>(value), timeToLive, cancellationToken).ConfigureAwait(false);
        return value;
    }

    /// <summary><c>null</c> və default dəyərləri də "hit" kimi saxlamaq üçün qab.</summary>
    public sealed record CacheBox<T>(T Value);
}

/// <summary>
/// Paylanmış kilid (çox-instanslı). Kilid müddəti bitəndə avtomatik açılır; açmaq yalnız sahibinin
/// token-i ilə mümkündür. Valkey əlçatmaz olanda kilid <b>alınmır</b> (təhlükəsiz tərəf).
/// </summary>
public interface IDistributedLock
{
    /// <summary>Kilidi almağa çalışır; alınmazsa <c>null</c>. Dispose kilidi buraxır.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken);
}

/// <summary>Bir rate limit yoxlamasının nəticəsi.</summary>
public sealed record RateLimitDecision(bool Allowed, int Remaining, TimeSpan RetryAfter);

/// <summary>
/// Çox-instanslı fixed-window rate limiter: bütün instanslar eyni sayğacı atomik Lua skripti ilə paylaşır.
/// Valkey əlçatmaz olanda sorğu buraxılır (fail-open) və bu loglanır.
/// </summary>
public interface IRateLimitStore
{
    Task<RateLimitDecision> TryAcquireAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken);
}
