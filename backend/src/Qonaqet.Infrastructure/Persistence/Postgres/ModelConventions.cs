using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Postgres;

/// <summary>Model konvensiyaları: snake_case adlar və soft delete qaydaları (öz kodumuz, əlavə paket yoxdur).</summary>
public static class ModelConventions
{
    public const string NotDeletedFilter = "is_deleted = false";

    /// <summary>
    /// Soft-deletable (append-only olmayan) hər entity-yə qlobal <c>!IsDeleted</c> filtri qoyur və onun
    /// bütün indekslərini partial edir (<c>WHERE is_deleted = false</c>), əgər konfiqurasiya başqa filtr
    /// verməyibsə.
    /// </summary>
    public static ModelBuilder ApplySoftDeleteConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType is null && IsSoftDeletable(e.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))),
                parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);

            foreach (var index in entityType.GetIndexes().Where(i => i.GetFilter() is null))
            {
                index.SetFilter(NotDeletedFilter);
            }
        }

        return modelBuilder;
    }

    /// <summary>Soft delete olunan, yəni append-only olmayan <see cref="ISoftDeletable"/>.</summary>
    public static bool IsSoftDeletable(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        return typeof(ISoftDeletable).IsAssignableFrom(clrType) && !typeof(IAppendOnly).IsAssignableFrom(clrType);
    }

    /// <summary>Cədvəl, sütun, açar, xarici açar və indeks adlarını snake_case edir.</summary>
    public static ModelBuilder ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table && entity.BaseType is null && !entity.IsOwned())
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            var tableId = entity.GetTableName() is { } t ? StoreObjectIdentifier.Table(t, entity.GetSchema()) : (StoreObjectIdentifier?)null;
            foreach (var property in entity.GetProperties())
            {
                var name = tableId is { } id ? property.GetColumnName(id) ?? property.Name : property.GetColumnName();
                property.SetColumnName(ToSnakeCase(name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? $"PK_{entity.GetTableName()}"));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? "fk"));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? "ix"));
            }
        }

        return modelBuilder;
    }

    /// <summary><c>CreatedAt</c> → <c>created_at</c>, <c>IX_AuditLogs_EntityId</c> → <c>ix_audit_logs_entity_id</c>.</summary>
    public static string ToSnakeCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var boundary = i > 0 && previous != '_' && (char.IsLower(previous) || char.IsDigit(previous) || char.IsLower(next));
                if (boundary)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
