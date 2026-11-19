using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Persistence.Mongo.Repositories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Seed;

/// <summary>
/// İlkin kateqoriyalar (<c>CLAUDE.md</c> → "B. Social treating" cədvəli): <c>everyone</c>, <c>student</c>, <c>veteran-family</c>,
/// <c>in-need</c>, hər birinin default hovuzu ilə. Bu, sadəcə başlanğıc datadır: admin sonra redaktə edə, deaktiv edə və yenisini
/// əlavə edə bilər. Kateqoriya kodları yalnız bu <c>Seed/</c> qovluğunda yazılır (arxitektura testi başqa yerdə qadağan edir).
/// </summary>
public static class CategorySeedData
{
    /// <summary>Tələbə kateqoriyasının <c>university</c> seçim sahəsi: Azərbaycanın ali təhsil müəssisələri.</summary>
    public static IReadOnlyList<string> Universities { get; } =
    [
        "Bakı Dövlət Universiteti",
        "ADA Universiteti",
        "Azərbaycan Dövlət İqtisad Universiteti (UNEC)",
        "Azərbaycan Texniki Universiteti",
        "Azərbaycan Dövlət Neft və Sənaye Universiteti",
        "Bakı Ali Neft Məktəbi",
        "Bakı Mühəndislik Universiteti",
        "Azərbaycan Tibb Universiteti",
        "Azərbaycan Dövlət Pedaqoji Universiteti",
        "Azərbaycan Dillər Universiteti",
        "Bakı Slavyan Universiteti",
        "Azərbaycan Memarlıq və İnşaat Universiteti",
        "Azərbaycan Dövlət Mədəniyyət və İncəsənət Universiteti",
        "Azərbaycan Dövlət Rəssamlıq Akademiyası",
        "Bakı Musiqi Akademiyası",
        "Azərbaycan Milli Konservatoriyası",
        "Azərbaycan Dövlət Bədən Tərbiyəsi və İdman Akademiyası",
        "Azərbaycan Respublikası Prezidenti yanında Dövlət İdarəçilik Akademiyası",
        "Milli Aviasiya Akademiyası",
        "Azərbaycan Dövlət Dəniz Akademiyası",
        "Azərbaycan Turizm və Menecment Universiteti",
        "Azərbaycan Kooperasiya Universiteti",
        "Fransız-Azərbaycan Universiteti (UFAZ)",
        "Moskva Dövlət Universitetinin Bakı filialı",
        "Azərbaycan Universiteti",
        "Xəzər Universiteti",
        "Qərb Universiteti",
        "Odlar Yurdu Universiteti",
        "Bakı Avrasiya Universiteti",
        "Bakı Biznes Universiteti",
        "Azərbaycan İlahiyyat İnstitutu",
        "Gəncə Dövlət Universiteti",
        "Azərbaycan Dövlət Aqrar Universiteti",
        "Azərbaycan Texnologiya Universiteti",
        "Sumqayıt Dövlət Universiteti",
        "Mingəçevir Dövlət Universiteti",
        "Lənkəran Dövlət Universiteti",
        "Naxçıvan Dövlət Universiteti",
        "Qarabağ Universiteti",
        "Digər",
    ];

    public sealed record Entry(string Code, CategoryDetails Details);

    public static IReadOnlyList<Entry> Categories { get; } = Build();

    private static List<Entry> Build() =>
    [
        new("everyone", new CategoryDetails(
            "Hamı", "Hər kəs üçün açıq qonaqlıq: təsdiq tələb olunmur.", null, "#FFB72B", 10, IsActive: true, IsOpen: true,
            CategoryPrivacyLevel.Public, VerificationConfig.NotRequired, CategoryLimits.Create(1, 15, poolAccess: false, 0))),

        new("student", new CategoryDetails(
            "Tələbələr", "Ali məktəb tələbələri üçün ısmarlamalar.", null, "#16B89B", 20, IsActive: true, IsOpen: false,
            CategoryPrivacyLevel.Public,
            VerificationConfig.Create(
                requiresFin: true, minFiles: 1, maxFiles: 3,
                documentHint: "e-gov.az-dan tələbə arayışı və ya tələbə bileti (PDF, JPG və ya PNG, hər biri ən çox 5 MB).",
                subtypes: [],
                fields: [VerificationField.Create("university", "Universitet", VerificationFieldType.Select, required: true, Universities)],
                validity: CategoryValidity.FixedDate(9, 30)),
            CategoryLimits.Create(2, 40, poolAccess: true, 500))),

        new("veteran-family", new CategoryDetails(
            "Qazilər və şəhid ailələri", "Qazilərimizə və şəhid ailələrinə hörmət əlaməti.", null, "#E8475F", 30, IsActive: true, IsOpen: false,
            CategoryPrivacyLevel.Sensitive,
            VerificationConfig.Create(
                requiresFin: true, minFiles: 1, maxFiles: 3,
                documentHint: "e-sosial.az-dan status arayışı (PDF, JPG və ya PNG, hər biri ən çox 5 MB).",
                subtypes: [CategorySubtype.Create("qazi", "Qazi"), CategorySubtype.Create("martyr-family", "Şəhid ailəsi")],
                fields: [],
                validity: CategoryValidity.ForMonths(12)),
            CategoryLimits.Create(3, 60, poolAccess: true, 1500))),

        new("in-need", new CategoryDetails(
            "Dəstəyə ehtiyacı olanlar", "Çətin gündə olan qonaqlarımız üçün.", null, "#2A1A3E", 40, IsActive: true, IsOpen: false,
            CategoryPrivacyLevel.Sensitive,
            VerificationConfig.Create(
                requiresFin: true, minFiles: 1, maxFiles: 3,
                documentHint: "ÜSY (ünvanlı sosial yardım) arayışı və ya bələdiyyə/QHT məktubu (PDF, JPG və ya PNG, hər biri ən çox 5 MB).",
                subtypes: [],
                fields: [],
                validity: CategoryValidity.ForMonths(6)),
            CategoryLimits.Create(3, 60, poolAccess: true, 1500))),
    ];
}

/// <summary>
/// Idempotent seed: kod üzrə heç bir sənəd (silinmiş də daxil) yoxdursa kateqoriya və default hovuzu bir tranzaksiyada yaradır.
/// Admin seed kateqoriyasını silibsə və ya dəyişibsə, seed ona toxunmur. Mövcud kateqoriyanın default hovuzu yoxdursa yaradılır.
/// </summary>
public sealed partial class CategorySeeder(IMongoClient client, IMongoDatabase database, IClock clock, ILogger<CategorySeeder> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var categories = database.GetCollection<Category>(CategoryCollectionNames.Categories);
        var pools = database.GetCollection<Pool>(CategoryCollectionNames.Pools);
        var created = 0;
        foreach (var entry in CategorySeedData.Categories)
        {
            using var session = await client.StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            session.StartTransaction();
            var existingWasMissing = false;
            try
            {
                var existing = await categories.Find(session, Builders<Category>.Filter.Eq(c => c.Code, entry.Code))
                    .SortBy(c => c.IsDeleted).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                var now = clock.UtcNow;
                if (existing is null)
                {
                    var category = Category.Create(entry.Code, entry.Details, now, null);
                    await categories.InsertOneAsync(session, category, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await pools.InsertOneAsync(session, Pool.CreateDefault(category.Id, now, null), cancellationToken: cancellationToken).ConfigureAwait(false);
                    created++;
                    existingWasMissing = true;
                }
                else if (!existing.IsDeleted)
                {
                    var poolFilter = Builders<Pool>.Filter.And(
                        Builders<Pool>.Filter.Eq(p => p.IsDeleted, false),
                        Builders<Pool>.Filter.Eq(p => p.CategoryId, existing.Id),
                        Builders<Pool>.Filter.Eq(p => p.CampaignId, null));
                    if (!await pools.Find(session, poolFilter).AnyAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await pools.InsertOneAsync(session, Pool.CreateDefault(existing.Id, now, null), cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }

                await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                if (existingWasMissing)
                {
                    LogSeeded(entry.Code);
                }
            }
            catch (MongoException ex) when (IsConcurrentSeed(ex))
            {
                // Başqa host (Api/Worker) eyni anda eyni kateqoriyanı seed etdi: unikal indeks qorudu, bu keçid atlanır.
                if (session.IsInTransaction)
                {
                    await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                }

                created -= existingWasMissing ? 1 : 0;
                LogConcurrent(entry.Code);
            }
            catch
            {
                if (session.IsInTransaction)
                {
                    await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
        }

        return created;
    }

    private static bool IsConcurrentSeed(MongoException ex) =>
        ex.HasErrorLabel("TransientTransactionError")
        || ex is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
        || ex is MongoCommandException { Code: 11000 or 112 };

    [LoggerMessage(Level = LogLevel.Information, Message = "Kateqoriya artıq başqa host tərəfindən seed olunur: {Code}")]
    private partial void LogConcurrent(string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kateqoriya seed olundu: {Code}")]
    private partial void LogSeeded(string code);
}
