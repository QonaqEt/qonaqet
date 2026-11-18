using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Messaging.Outbox;
using StackExchange.Redis;

namespace Qonaqet.Infrastructure.Caching;

/// <summary><c>Valkey</c> konfiqurasiya bölməsi.</summary>
public sealed class ValkeyOptions
{
    public const string SectionName = "Valkey";

    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Bütün açarların prefiksi (bir Valkey-i bir neçə mühit paylaşa bilsin).</summary>
    public string KeyPrefix { get; set; } = "qonaqet:";
}

/// <summary>
/// Tənbəl açılan <see cref="IConnectionMultiplexer"/>. <c>AbortOnConnectFail = false</c>: Valkey qalxmayıbsa
/// host yenə də işləyir, klient arxa planda yenidən qoşulur.
/// </summary>
public sealed class ValkeyConnection(IOptions<ValkeyOptions> options) : IAsyncDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection = new(async () =>
    {
        var config = ConfigurationOptions.Parse(options.Value.ConnectionString);
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 2000;
        config.SyncTimeout = 2000;
        config.AsyncTimeout = 2000;
        return await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
    });

    public string Prefix => options.Value.KeyPrefix;

    public async Task<IDatabase> DatabaseAsync() => (await _connection.Value.ConfigureAwait(false)).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            await (await _connection.Value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>JSON keş. Xətalar "miss" kimi qəbul olunur — keş heç vaxt həqiqət mənbəyi deyil.</summary>
public sealed partial class ValkeyCache(ValkeyConnection connection, ILogger<ValkeyCache> logger) : ICache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var value = await db.StringGetAsync(connection.Prefix + key).WaitAsync(cancellationToken).ConfigureAwait(false);
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString(), OutboxSerializer.JsonOptions);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            LogUnavailable(ex, "get");
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var json = JsonSerializer.Serialize(value, OutboxSerializer.JsonOptions);
            await db.StringSetAsync(connection.Prefix + key, json, timeToLive).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            LogUnavailable(ex, "set");
        }
    }

    public async Task InvalidateAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            await db.KeyExpireAsync(connection.Prefix + key, TimeSpan.Zero).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            LogUnavailable(ex, "invalidate");
        }
    }

    internal static bool IsTransient(Exception ex) => ex is RedisException or TimeoutException or JsonException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey keşi əlçatan deyil ({Operation}); əsas store işlədilir")]
    private partial void LogUnavailable(Exception exception, string operation);
}

/// <summary><c>SET NX PX</c> + token ilə buraxma (Lua: yalnız sahibi açır).</summary>
public sealed partial class ValkeyDistributedLock(ValkeyConnection connection, ILogger<ValkeyDistributedLock> logger) : IDistributedLock
{
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('PEXPIRE', KEYS[1], 1)
        end
        return 0
        """;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var key = connection.Prefix + "lock:" + resource;
        var token = Guid.NewGuid().ToString("N");
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var acquired = await db.StringSetAsync(key, token, expiry, When.NotExists).WaitAsync(cancellationToken).ConfigureAwait(false);
            return acquired ? new Handle(db, key, token) : null;
        }
        catch (Exception ex) when (ValkeyCache.IsTransient(ex))
        {
            LogUnavailable(ex, resource);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey əlçatan deyil: {Resource} kilidi alınmadı")]
    private partial void LogUnavailable(Exception exception, string resource);

    private sealed class Handle(IDatabase db, string key, string token) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return;
            }

            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]).ConfigureAwait(false);
            }
            catch (Exception ex) when (ValkeyCache.IsTransient(ex))
            {
                // Kilid müddəti bitəndə özü açılacaq.
            }
        }
    }
}

/// <summary>
/// Öz Lua skriptimiz: <c>INCR</c> + ilk sorğuda <c>PEXPIRE</c> atomik icra olunur, ona görə bütün instanslar
/// eyni pəncərəni bölüşür. Valkey əlçatmaz olanda fail-open.
/// </summary>
public sealed partial class ValkeyRateLimitStore(ValkeyConnection connection, ILogger<ValkeyRateLimitStore> logger) : IRateLimitStore
{
    private const string Script = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            ttl = tonumber(ARGV[2])
        end
        return { current, ttl }
        """;

    public async Task<RateLimitDecision> TryAcquireAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var result = (RedisResult[])(await db.ScriptEvaluateAsync(
                Script, [connection.Prefix + "rl:" + partitionKey], [permitLimit, (long)window.TotalMilliseconds])
                .WaitAsync(cancellationToken).ConfigureAwait(false))!;
            var count = (long)result[0];
            var ttl = TimeSpan.FromMilliseconds((long)result[1]);
            return count <= permitLimit
                ? new RateLimitDecision(true, (int)(permitLimit - count), TimeSpan.Zero)
                : new RateLimitDecision(false, 0, ttl);
        }
        catch (Exception ex) when (ValkeyCache.IsTransient(ex))
        {
            LogUnavailable(ex, partitionKey);
            return new RateLimitDecision(true, permitLimit, TimeSpan.Zero);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey əlçatan deyil: {Partition} üçün rate limit yoxlanmadı (fail-open)")]
    private partial void LogUnavailable(Exception exception, string partition);
}
