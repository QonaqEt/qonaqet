using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary>
/// Aqreqat repository-si. <b>Hər</b> oxu yalnız silinməmiş qeydləri görür (<c>isDeleted: false</c>);
/// silinmiş qeydlər yalnız admin ssenarilərində <c>IncludeDeleted()</c> ilə görünür.
/// Fiziki silmə yoxdur: <see cref="SoftDeleteAsync"/> və <see cref="RestoreAsync"/> audit log yazır.
/// </summary>
public interface IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    /// <summary>Silinməmiş aqreqat; yoxdursa və ya silinibsə <c>null</c>.</summary>
    Task<TAggregate?> GetAsync(TId id, CancellationToken cancellationToken);

    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken);

    /// <summary>Silinməmiş aqreqatı əvəz edir; tapılmasa <see cref="InvalidOperationException"/>.</summary>
    Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken);

    /// <summary>Soft delete + audit log. Qeyd yoxdursa və ya artıq silinibsə <c>false</c>.</summary>
    Task<bool> SoftDeleteAsync(TId id, Guid? userId, string? reason, CancellationToken cancellationToken);

    /// <summary>Soft delete-i geri qaytarır + audit log. Silinmiş qeyd yoxdursa <c>false</c>.</summary>
    Task<bool> RestoreAsync(TId id, Guid? userId, CancellationToken cancellationToken);
}
