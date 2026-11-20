using System.Text.RegularExpressions;
using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Social;

public readonly record struct CampaignId(Guid Value) : IStronglyTypedId<CampaignId>
{
    public static CampaignId From(Guid value) => new(value);
}

public enum CampaignStatus
{
    Draft,
    Active,
    Ended,
}

/// <summary>Kampaniyanın redaktə olunan sahələri.</summary>
public sealed record CampaignDetails(
    string Title, string Slug, string Description, Guid? BannerFileId, CategoryId CategoryId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, long GoalQepik);

/// <summary>
/// Kampaniya (<c>campaigns</c>): bir kateqoriya üçün müddətli ianə toplama, öz hovuzu ilə. <c>Draft</c> → <c>Active</c> (aktivləşdiriləndə hovuz
/// yaranır) → <c>Ended</c> (müddət bitəndə job). Bitəndən sonra hovuzun istifadə olunmamış qalığı kateqoriyanın default hovuzuna
/// <c>CampaignRollover</c> ledger yazısı ilə köçür. <see cref="RaisedQepik"/> təsdiqlənmiş ianələrin cəmidir.
/// </summary>
public sealed partial class Campaign : AggregateRoot<CampaignId>
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const long MaxGoalQepik = 100_000_000;

    private Campaign(CampaignId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Campaign()
    {
    }

    public string Title { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Guid? BannerFileId { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public long GoalQepik { get; private set; }

    public long RaisedQepik { get; private set; }

    /// <summary>Default hovuza köçürülmüş qalıq (cəm).</summary>
    public long RolledOverQepik { get; private set; }

    public Guid? CorporateId { get; private set; }

    public CampaignStatus Status { get; private set; }

    /// <summary>Kampaniyanın hovuzu (aktivləşdiriləndə yaranır).</summary>
    public PoolId? PoolId { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public static bool IsValidSlug(string? slug) => slug is { Length: >= 3 and <= 80 } && SlugPattern().IsMatch(slug);

    public bool IsOpenAt(DateTimeOffset now) => Status == CampaignStatus.Active && now >= StartsAt && now < EndsAt;

    /// <summary>Məqsədin faizi (0–100+, tam).</summary>
    public int ProgressPercent => GoalQepik <= 0 ? 0 : (int)Math.Min(int.MaxValue, RaisedQepik * 100 / GoalQepik);

    public static Campaign CreateDraft(CampaignDetails details, Guid? corporateId, DateTimeOffset at, Guid? by)
    {
        var campaign = new Campaign(StronglyTypedId.New<CampaignId>(), at, by) { Status = CampaignStatus.Draft, CorporateId = corporateId };
        campaign.Apply(details);
        return campaign;
    }

    /// <summary>Sahələri dəyişir (bitmiş kampaniya dəyişmir; aktiv kampaniyanın kateqoriyası dəyişmir — hovuzu ona bağlıdır).</summary>
    public void Update(CampaignDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status == CampaignStatus.Ended || (Status == CampaignStatus.Active && details.CategoryId != CategoryId))
        {
            throw new InvalidOperationException("Bu kampaniya dəyişdirilə bilməz.");
        }

        Apply(details);
        MarkUpdated(at, by);
    }

    /// <summary><c>Draft</c> → <c>Active</c>; hovuz çağıran tərəfindən yaradılır.</summary>
    public void Activate(PoolId poolId, DateTimeOffset at, Guid? by)
    {
        if (Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException("Yalnız qaralama kampaniya aktivləşdirilə bilər.");
        }

        if (EndsAt <= at)
        {
            throw new InvalidOperationException("Bitmə tarixi keçib.");
        }

        Status = CampaignStatus.Active;
        PoolId = poolId;
        MarkUpdated(at, by);
    }

    private void Apply(CampaignDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!IsValidSlug(details.Slug))
        {
            throw new ArgumentException("Slug düzgün deyil.", nameof(details));
        }

        if (details.EndsAt <= details.StartsAt)
        {
            throw new ArgumentException("Bitmə tarixi başlanğıcdan sonra olmalıdır.", nameof(details));
        }

        if (details.GoalQepik is <= 0 or > MaxGoalQepik)
        {
            throw new ArgumentOutOfRangeException(nameof(details), "Məqsəd məbləği düzgün deyil.");
        }

        Title = Guard.NotNullOrWhiteSpace(details.Title).Trim();
        Slug = details.Slug;
        Description = details.Description?.Trim() ?? string.Empty;
        BannerFileId = details.BannerFileId;
        CategoryId = details.CategoryId;
        StartsAt = Guard.Utc(details.StartsAt);
        EndsAt = Guard.Utc(details.EndsAt);
        GoalQepik = details.GoalQepik;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

/// <summary>Bitmiş kampaniyanın qalığı default hovuza köçdü → ledger-də <c>CampaignRollover</c> (kampaniya hovuzundan −, default hovuza +).</summary>
public sealed record CampaignRolledOver(CampaignId CampaignId, PoolId FromPoolId, PoolId ToPoolId, long AmountQepik, DateTimeOffset At) : DomainEvent(At);
