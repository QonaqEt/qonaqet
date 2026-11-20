using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Social;

public readonly record struct PoolId(Guid Value) : IStronglyTypedId<PoolId>
{
    public static PoolId From(Guid value) => new(value);
}

/// <summary>
/// Kateqoriyanın ianə hovuzu (<c>pools</c>). Hər kateqoriyanın bir <b>default</b> hovuzu var (kateqoriya yaradılanda
/// avtomatik yaranır, <see cref="CampaignId"/> boşdur); kampaniyaların öz hovuzları olur (E08). Balans Mongo-da
/// əməliyyat dəyəridir, ledger (Postgres) isə mənbədir — gecəlik uzlaşdırma onları müqayisə edir.
/// Pul hərəkətləri (ianə, rezerv, claim) E08-dədir.
/// </summary>
public sealed class Pool : AggregateRoot<PoolId>
{
    private Pool(PoolId id, CategoryId categoryId, Guid? campaignId, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        CategoryId = categoryId;
        CampaignId = campaignId;
    }

    private Pool()
    {
    }

    public CategoryId CategoryId { get; private set; }

    /// <summary>Kampaniya hovuzu üçün kampaniya; default hovuzda <c>null</c>.</summary>
    public Guid? CampaignId { get; private set; }

    public long BalanceQepik { get; private set; }

    /// <summary>Aktiv claim-lər üçün ayrılmış məbləğ (balansın içindədir).</summary>
    public long ReservedQepik { get; private set; }

    public bool IsDefault => CampaignId is null;

    /// <summary>Kateqoriyanın default hovuzu, boş balansla.</summary>
    public static Pool CreateDefault(CategoryId categoryId, DateTimeOffset at, Guid? by) =>
        new(StronglyTypedId.New<PoolId>(), categoryId, null, at, by);

    /// <summary>Kampaniyanın hovuzu (kampaniya aktivləşdiriləndə), boş balansla.</summary>
    public static Pool CreateForCampaign(CategoryId categoryId, Guid campaignId, DateTimeOffset at, Guid? by) =>
        new(StronglyTypedId.New<PoolId>(), categoryId, Guard.NotEmpty(campaignId), at, by);
}
