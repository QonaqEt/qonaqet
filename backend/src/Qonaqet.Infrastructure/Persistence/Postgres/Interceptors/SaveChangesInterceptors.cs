using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Interceptors;

/// <summary>Sync və async <c>SaveChanges</c> üçün eyni məntiq.</summary>
public abstract class SavingInterceptor : SaveChangesInterceptor
{
    public sealed override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            Apply(context);
        }

        return result;
    }

    public sealed override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            Apply(context);
        }

        return ValueTask.FromResult(result);
    }

    protected abstract void Apply(DbContext context);
}

/// <summary>
/// Fiziki silməni qadağan edir: <c>Deleted</c> vəziyyəti soft delete-ə (<c>Modified</c>) çevrilir.
/// Append-only və ya soft delete dəstəkləməyən entity silinərsə exception atır.
/// </summary>
public sealed class SoftDeleteInterceptor(IClock clock, ICurrentUser? currentUser = null) : SavingInterceptor
{
    protected override void Apply(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted).ToList())
        {
            if (entry.Entity is IAppendOnly || entry.Entity is not ISoftDeletableEntity entity)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} append-only-dir və ya soft delete dəstəkləmir: silinə bilməz (CLAUDE.md → Soft delete).");
            }

            entry.State = EntityState.Unchanged;
            entity.SoftDelete(clock.UtcNow, currentUser?.ActorId, reason: null);
            entry.State = EntityState.Modified;
        }
    }
}

/// <summary>
/// Hər yaradılma/dəyişiklik/soft delete üçün eyni <c>SaveChanges</c>-də (eyni tranzaksiya) <c>audit_logs</c>
/// sətri əlavə edir. Yalnız dəyişən sahələrin <b>adları</b> yazılır, dəyərlər yox — şəxsi məlumat sızmasın.
/// </summary>
public sealed class AuditInterceptor(IClock clock, ICurrentUser? currentUser = null) : SavingInterceptor
{
    protected override void Apply(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = clock.UtcNow;
        var actorId = currentUser?.ActorId;
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not INotAudited && e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified && entry.Entity is ISoftDeletableEntity { IsAppendOnly: false } updatable)
            {
                updatable.MarkUpdated(now, actorId);
            }

            var changed = entry.State == EntityState.Added
                ? []
                : entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order(StringComparer.Ordinal).ToArray();

            context.Add(new AuditLogRecord
            {
                Id = Guid.CreateVersion7(),
                ActorId = actorId,
                ActorType = actorId is null ? "System" : "User",
                Action = ActionOf(entry),
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = KeyOf(entry),
                Data = JsonSerializer.Serialize(new { changed }, OutboxSerializer.JsonOptions),
                CreatedAt = now,
            });
        }
    }

    private static string ActionOf(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return "Create";
        }

        var isDeleted = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted));
        if (isDeleted is not null && !Equals(isDeleted.OriginalValue, isDeleted.CurrentValue))
        {
            return isDeleted.CurrentValue is true ? "SoftDelete" : "Restore";
        }

        return "Update";
    }

    private static Guid? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is not { Properties.Count: 1 })
        {
            return null;
        }

        return entry.Property(key.Properties[0].Name).CurrentValue switch
        {
            Guid g => g,
            IStronglyTypedId id => id.Value,
            _ => null,
        };
    }
}

/// <summary>İzlənən aqreqatların domen hadisələrini eyni <c>SaveChanges</c>-də <c>outbox_messages</c>-ə yazır.</summary>
public sealed class OutboxInterceptor : SavingInterceptor
{
    protected override void Apply(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var events = context.ChangeTracker.Entries<IAggregateRoot>()
            .SelectMany(e => e.Entity.DequeueDomainEvents())
            .ToList();

        foreach (var domainEvent in events)
        {
            context.Add(ToMessage(domainEvent));
        }
    }

    public static PostgresOutboxMessage ToMessage(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return new PostgresOutboxMessage
        {
            Id = domainEvent.EventId,
            Type = OutboxSerializer.TypeName(domainEvent),
            Payload = OutboxSerializer.Serialize(domainEvent),
            OccurredAt = domainEvent.OccurredAt,
        };
    }
}
