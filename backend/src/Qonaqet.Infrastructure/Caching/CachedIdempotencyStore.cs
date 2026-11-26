using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Caching;

/// <summary>
/// Idempotency store-u: həqiqət mənbəyi <c>idempotency_records</c> (Postgres, tarixçə), Valkey yalnız oxu keşidir (24 saat). Keş boş və ya
/// əlçatmaz olanda Postgres oxunur; yazı həmişə əvvəl Postgres-ə gedir.
/// </summary>
public sealed class CachedIdempotencyStore(PostgresIdempotencyStore inner, ICache cache) : IIdempotencyStore
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<IdempotencyRecord>(CacheKey(scope, key), cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var record = await inner.FindAsync(scope, key, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            await cache.SetAsync(CacheKey(scope, key), record, CacheLifetime, cancellationToken).ConfigureAwait(false);
        }

        return record;
    }

    public async Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = await inner.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await cache.SetAsync(CacheKey(stored.Scope, stored.Key), stored, CacheLifetime, cancellationToken).ConfigureAwait(false);
        return stored;
    }

    private static string CacheKey(string scope, string key) => $"idem:{scope}:{key}";
}
