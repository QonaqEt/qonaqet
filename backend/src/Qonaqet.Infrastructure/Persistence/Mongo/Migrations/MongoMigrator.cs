using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

/// <summary>
/// Startup-da idempotent Mongo migrasiyası: (1) <c>isDeleted</c> sahəsi olmayan sənədlərə
/// <c>isDeleted: false</c> yazır (köhnə qeydlər partial indekslərdən və sorğulardan düşməsin deyə),
/// (2) indeksləri yaradır və ziddiyyətli köhnə indeksləri atır.
/// </summary>
public sealed partial class MongoMigrator(IMongoDatabase database, MongoIndexRunner indexRunner, ILogger<MongoMigrator> logger)
{
    public async Task<MongoIndexRunResult> RunAsync(CancellationToken cancellationToken)
    {
        foreach (var name in MongoIndexCatalog.SoftDeletableCollections.Concat(MongoIndexCatalog.AppendOnlyCollections))
        {
            var updated = await BackfillIsDeletedAsync(name, cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                LogBackfilled(name, updated);
            }
        }

        return await indexRunner.RunAsync(MongoIndexCatalog.All, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> BackfillIsDeletedAsync(string collectionName, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var filter = Builders<BsonDocument>.Filter.Exists("isDeleted", false);
        var update = Builders<BsonDocument>.Update.Set("isDeleted", false);
        var result = await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Collection}: {Count} sənədə isDeleted=false yazıldı")]
    private partial void LogBackfilled(string collection, long count);
}

/// <summary>
/// <c>Mongo:ApplyMigrationsOnStartup = true</c> olduqda migrasiyanı host qalxarkən işə salır, sonra
/// (<c>Mongo:SeedReferenceData</c>) ilkin kateqoriyaları seed edir; <c>Mongo:SeedDemoData</c> (yalnız dev) demo datasını əlavə edir.
/// </summary>
public sealed class MongoMigrationHostedService(
    MongoMigrator migrator, Seed.CategorySeeder seeder, Seed.CampaignSeeder campaignSeeder, Seed.PlusDemoSeeder plusSeeder, IOptions<MongoOptions> options)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        await migrator.RunAsync(cancellationToken).ConfigureAwait(false);
        if (options.Value.SeedReferenceData)
        {
            await seeder.RunAsync(cancellationToken).ConfigureAwait(false);
            await campaignSeeder.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        if (options.Value.SeedDemoData)
        {
            await plusSeeder.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
