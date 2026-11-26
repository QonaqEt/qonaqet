namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary><c>Mongo</c> konfiqurasiya bölməsi.</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    /// <summary>Replica set tələb olunur (tranzaksiyalar), məs. <c>mongodb://mongo:27017/?replicaSet=rs0</c>.</summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?directConnection=true";

    public string Database { get; set; } = "qonaqet";

    /// <summary>
    /// Startup-da idempotent migrasiya (isDeleted backfill + indekslər). Docker compose bunu açır;
    /// testlər və konfiqurasiyasız host-lar üçün default bağlıdır ki, startup Mongo gözləməsin.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>
    /// Migrasiyadan sonra ilkin istinad datasını (4 kateqoriya + default hovuzları) idempotent seed edir. Yalnız
    /// <see cref="ApplyMigrationsOnStartup"/> açıq olduqda işləyir; bütün mühitlərdə default açıqdır.
    /// </summary>
    public bool SeedReferenceData { get; set; } = true;

    /// <summary>
    /// Development demo datası (QONAQET Plus brendi və kuponları). Default bağlıdır; yalnız dev docker-compose açır — staging/production-da
    /// heç vaxt.
    /// </summary>
    public bool SeedDemoData { get; set; }
}
