using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Persistence.Mongo.Repositories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Seed;

/// <summary>4 kampaniya şablonu (CLAUDE.md → F. Other): hamısı <c>Draft</c>; admin tarixləri/məqsədi dəqiqləşdirib aktivləşdirir.</summary>
public static class CampaignSeedData
{
    public sealed record Template(string Slug, string Title, string Description, string CategoryCode, int Month, int StartDay, int EndDay, long GoalQepik);

    // Kateqoriya kodu yalnız seed məlumatıdır (biznes məntiqi kod üzrə şaxələnmir). Ramazanın tarixi hər il dəyişir — admin düzəldir.
    public static IReadOnlyList<Template> Templates { get; } =
    [
        new("ramazan", "Ramazan süfrəsi", "Ramazan ayında ehtiyacı olanlara iftar süfrəsi.", "in-need", 2, 8, 9 + 28, 5_000_00),
        new("novruz", "Novruz bayramı", "Bayram süfrəsini hamı ilə bölüşək.", "everyone", 3, 17, 24, 3_000_00),
        new("27-sentyabr-anim-gunu", "27 Sentyabr — Anım Günü", "Şəhid ailələrinə və qazilərə hörmət.", "veteran-family", 9, 20, 28, 5_000_00),
        new("8-noyabr-zefer-gunu", "8 Noyabr — Zəfər Günü", "Qazilərimizə və şəhid ailələrinə təşəkkür.", "veteran-family", 11, 1, 9, 5_000_00),
    ];
}

/// <summary>
/// Idempotent seed: slug üzrə heç bir sənəd (silinmiş də daxil) yoxdursa şablon <c>Draft</c> kimi yaradılır. Admin şablonu silibsə və ya
/// dəyişibsə, seed ona toxunmur. Kateqoriyalardan sonra işləyir.
/// </summary>
public sealed partial class CampaignSeeder(IMongoDatabase database, IClock clock, ILogger<CampaignSeeder> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var campaigns = database.GetCollection<Campaign>(DonationCollectionNames.Campaigns);
        var categories = database.GetCollection<Category>(CategoryCollectionNames.Categories);
        var now = clock.UtcNow;
        var created = 0;
        foreach (var template in CampaignSeedData.Templates)
        {
            if (await campaigns.Find(Builders<Campaign>.Filter.Eq(c => c.Slug, template.Slug)).AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var category = await categories.Find(c => c.Code == template.CategoryCode && !c.IsDeleted).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (category is null)
            {
                continue;
            }

            var year = BakuTime.ToBakuDate(now).Month > template.Month ? BakuTime.ToBakuDate(now).Year + 1 : BakuTime.ToBakuDate(now).Year;
            var starts = BakuTime.StartOfDayUtc(new DateOnly(year, template.Month, template.StartDay));
            var ends = BakuTime.StartOfDayUtc(new DateOnly(year, template.Month, 1).AddDays(template.EndDay));
            var campaign = Campaign.CreateDraft(
                new CampaignDetails(template.Title, template.Slug, template.Description, null, category.Id, starts, ends, template.GoalQepik),
                corporateId: null, now, by: null);
            try
            {
                await campaigns.InsertOneAsync(campaign, cancellationToken: cancellationToken).ConfigureAwait(false);
                created++;
                LogSeeded(template.Slug);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Başqa host eyni anda seed etdi (unikal slug indeksi).
            }
        }

        return created;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Kampaniya şablonu seed olundu: {Slug}")]
    private partial void LogSeeded(string slug);
}
