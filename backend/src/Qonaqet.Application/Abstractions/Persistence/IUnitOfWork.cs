using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary>
/// Bir store daxilində tranzaksiya (Mongo və ya Postgres; heç vaxt distributed deyil).
/// UnitOfWork behavior hər əmr üçün açır, outbox-u eyni tranzaksiyada yazır və commit edir.
/// </summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Postgres tranzaksiyası (ödənişlər, ledger, payout-lar). <see cref="Abstractions.Messaging.IPostgresCommand"/> əmrləri default
/// (Mongo) əvəzinə bunu açır; hadisələr Postgres outbox-una interceptor ilə eyni <c>SaveChanges</c>-də yazılır.
/// </summary>
public interface IPostgresUnitOfWork : IUnitOfWork;

/// <summary>Bu sorğuda yüklənən/yaradılan aqreqatlar. Repository-lər qeyd edir, UnitOfWork hadisələri buradan toplayır.</summary>
public interface IAggregateTracker
{
    void Track(IAggregateRoot aggregate);

    IReadOnlyCollection<IAggregateRoot> Tracked { get; }
}

/// <summary>Domen hadisələrini cari tranzaksiyada outbox-a yazır (relay Worker-dədir, E01-T09).</summary>
public interface IOutbox
{
    Task AddAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}

/// <summary>Idempotency cavablarının saxlanması (Postgres <c>idempotency_records</c>, Valkey yalnız keş).</summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken);

    /// <summary>Qeydi əlavə edir; eyni scope + key artıq varsa mövcud qeydi qaytarır (yarış vəziyyəti üçün).</summary>
    Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}

/// <summary>Saxlanmış idempotent cavab. <see cref="ResponseJson"/> yalnız uğurlu cavabın dəyəridir.</summary>
public sealed record IdempotencyRecord(string Scope, string Key, string RequestHash, string ResponseJson, DateTimeOffset CreatedAt);
