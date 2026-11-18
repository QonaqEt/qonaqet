namespace Qonaqet.SharedKernel;

/// <summary>Güclü tipli identifikatorun qeyri-generik görünüşü (serializer-lər üçün).</summary>
public interface IStronglyTypedId
{
    Guid Value { get; }
}

/// <summary>
/// Güclü tipli identifikator (<c>StronglyTypedId&lt;T&gt;</c>): <see cref="Guid"/> üzərində
/// <c>readonly record struct</c>. Nümunə:
/// <code>public readonly record struct GiftId(Guid Value) : IStronglyTypedId&lt;GiftId&gt;
/// {
///     public static GiftId From(Guid value) => new(value);
/// }</code>
/// JSON (<see cref="StronglyTypedIdJsonConverterFactory"/>), Mongo və EF çeviriciləri bu interfeys
/// üzərində ümumi işləyir, ona görə hər yeni ID üçün əlavə kod lazım deyil.
/// </summary>
public interface IStronglyTypedId<TSelf> : IStronglyTypedId
    where TSelf : struct, IStronglyTypedId<TSelf>
{
    static abstract TSelf From(Guid value);
}

/// <summary>Güclü tipli ID köməkçiləri.</summary>
public static class StronglyTypedId
{
    /// <summary>Yeni, vaxta görə sıralanan (UUID v7) identifikator.</summary>
    public static TId New<TId>()
        where TId : struct, IStronglyTypedId<TId> => TId.From(Guid.CreateVersion7());

    /// <summary>Mövcud dəyərdən ID; boş <see cref="Guid"/> qəbul edilmir.</summary>
    public static TId From<TId>(Guid value)
        where TId : struct, IStronglyTypedId<TId> => TId.From(Guard.NotEmpty(value));

    /// <summary>Tipin güclü tipli ID olub-olmadığını yoxlayır (serializer qeydiyyatı üçün).</summary>
    public static bool IsStronglyTypedId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsValueType && type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>) && i.GetGenericArguments()[0] == type);
    }
}
