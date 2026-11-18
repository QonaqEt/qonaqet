using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Postgres;

/// <summary>Güclü tipli ID ↔ <c>uuid</c> sütunu.</summary>
/// <remarks>Expression tree static abstract üzvü çağıra bilmədiyi üçün yaradılma <see cref="Create"/> üzərindən gedir.</remarks>
public sealed class StronglyTypedIdValueConverter<TId>() : ValueConverter<TId, Guid>(id => id.Value, value => Create(value))
    where TId : struct, IStronglyTypedId<TId>
{
    private static TId Create(Guid value) => TId.From(value);
}

public static class StronglyTypedIdConventions
{
    private static readonly MethodInfo ConfigureOneMethod =
        typeof(StronglyTypedIdConventions).GetMethod(nameof(ConfigureOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Verilən assembly-lərdəki bütün güclü tipli ID-lər üçün çeviricini konvensiya kimi qeyd edir
    /// (<c>DbContext.ConfigureConventions</c>-dan çağırılır).
    /// </summary>
    public static ModelConfigurationBuilder AddStronglyTypedIdConversions(
        this ModelConfigurationBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(StronglyTypedId.IsStronglyTypedId).Distinct())
        {
            ConfigureOneMethod.MakeGenericMethod(type).Invoke(null, [builder]);
        }

        return builder;
    }

    private static void ConfigureOne<TId>(ModelConfigurationBuilder builder)
        where TId : struct, IStronglyTypedId<TId> =>
        builder.Properties<TId>().HaveConversion<StronglyTypedIdValueConverter<TId>>();
}
