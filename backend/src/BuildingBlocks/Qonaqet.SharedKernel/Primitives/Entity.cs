namespace Qonaqet.SharedKernel;

/// <summary>
/// Bütün entity-lərin bazası (<c>CLAUDE.md</c>-dəki <c>BaseEntity</c>): <c>id</c>, yaradılma/yenilənmə
/// auditi və soft delete sahələri. <see cref="IsDeleted"/> həmişə mövcuddur, default <c>false</c>.
/// </summary>
public abstract class Entity<TId> : ISoftDeletableEntity, IEquatable<Entity<TId>>
    where TId : struct, IStronglyTypedId<TId>
{
    protected Entity(TId id, DateTimeOffset createdAt, Guid? createdBy = null)
    {
        Guard.NotEmpty(id.Value, nameof(id));

        Id = id;
        CreatedAt = Guard.Utc(createdAt);
        CreatedBy = createdBy;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Persistence (Mongo class map / EF) üçün.</summary>
    protected Entity()
    {
    }

    public TId Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public string? DeleteReason { get; private set; }

    public bool IsAppendOnly => this is IAppendOnly;

    /// <summary>Dəyişiklik auditini yeniləyir (repository / interceptor çağırır).</summary>
    public void MarkUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = Guard.Utc(at);
        UpdatedBy = by;
    }

    /// <summary>
    /// Soft delete. Append-only qeydlərdə proqramçı xətasıdır. Təkrar çağırış heç nəyi dəyişmir
    /// (ilk silinmənin izi qorunur).
    /// </summary>
    public void SoftDelete(DateTimeOffset at, Guid? by, string? reason)
    {
        EnsureNotAppendOnly();
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = Guard.Utc(at);
        DeletedBy = by;
        DeleteReason = reason;
        MarkUpdated(at, by);
    }

    /// <summary>Soft delete-i geri qaytarır.</summary>
    public void Restore(DateTimeOffset at, Guid? by)
    {
        EnsureNotAppendOnly();
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
        DeleteReason = null;
        MarkUpdated(at, by);
    }

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    private void EnsureNotAppendOnly()
    {
        if (IsAppendOnly)
        {
            throw new InvalidOperationException($"{GetType().Name} append-only-dir: silinə və bərpa oluna bilməz.");
        }
    }
}
