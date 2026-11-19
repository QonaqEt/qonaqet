using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Verification;

public readonly record struct IdentityHashId(Guid Value) : IStronglyTypedId<IdentityHashId>
{
    public static IdentityHashId From(Guid value) => new(value);
}

/// <summary>
/// FIN-in bir kateqoriyada bir hesaba bağlanması (<c>identityHashes</c>). (<see cref="FinHash"/>, <see cref="CategoryId"/>) partial
/// unikaldır: eyni şəxsiyyət vəsiqəsi ilə ikinci hesab eyni kateqoriyanı ala bilməz (<c>identity_already_used</c>).
/// Xam FIN heç vaxt saxlanılmır. Sorğu rədd olunanda bağ soft delete olunur ki, FIN yenidən istifadə oluna bilsin.
/// </summary>
public sealed class IdentityHash : AggregateRoot<IdentityHashId>
{
    private IdentityHash(IdentityHashId id, string finHash, CategoryId categoryId, Guid userId, DateTimeOffset createdAt)
        : base(id, createdAt, userId)
    {
        FinHash = finHash;
        CategoryId = categoryId;
        UserId = userId;
    }

    private IdentityHash()
    {
    }

    public string FinHash { get; private set; } = string.Empty;

    public CategoryId CategoryId { get; private set; }

    public Guid UserId { get; private set; }

    public static IdentityHash Bind(string finHash, CategoryId categoryId, Guid userId, DateTimeOffset at) =>
        new(StronglyTypedId.New<IdentityHashId>(), Guard.NotNullOrWhiteSpace(finHash), categoryId, Guard.NotEmpty(userId), at);
}
