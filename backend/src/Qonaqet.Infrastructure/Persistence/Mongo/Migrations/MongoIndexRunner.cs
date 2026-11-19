using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

/// <summary>Index runner-in bir icrasının nəticəsi.</summary>
public sealed record MongoIndexRunResult(IReadOnlyList<string> Created, IReadOnlyList<string> Dropped, IReadOnlyList<string> Unchanged);

/// <summary>
/// İndeksləri idempotent yaradır: eyni ad + eyni spesifikasiya → toxunulmur; eyni açarlı, amma fərqli
/// (məs. köhnə qeyri-partial) və ya eyni adlı, fərqli açarlı indeks → atılır və düzgünü yaradılır.
/// Yalnız indeks atılır — sənədlər heç vaxt silinmir.
/// </summary>
public sealed partial class MongoIndexRunner(IMongoDatabase database, ILogger<MongoIndexRunner> logger)
{
    public async Task<MongoIndexRunResult> RunAsync(IEnumerable<MongoIndexDefinition> definitions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var created = new List<string>();
        var dropped = new List<string>();
        var unchanged = new List<string>();

        foreach (var group in definitions.GroupBy(d => d.Collection, StringComparer.Ordinal))
        {
            var collection = database.GetCollection<BsonDocument>(group.Key);
            var existing = await ListAsync(collection, cancellationToken).ConfigureAwait(false);

            foreach (var definition in group)
            {
                var label = $"{definition.Collection}.{definition.Name}";
                var conflicts = existing
                    .Where(e => e.GetValue("name", "").AsString != "_id_")
                    .Where(e => SameName(e, definition) || SameKeys(e, definition))
                    .ToList();

                if (conflicts.Count == 1 && SameName(conflicts[0], definition) && Matches(conflicts[0], definition))
                {
                    unchanged.Add(label);
                    continue;
                }

                foreach (var conflict in conflicts)
                {
                    var name = conflict["name"].AsString;
                    await collection.Indexes.DropOneAsync(name, cancellationToken).ConfigureAwait(false);
                    existing.Remove(conflict);
                    dropped.Add($"{definition.Collection}.{name}");
                    LogDropped(definition.Collection, name);
                }

                await collection.Indexes.CreateOneAsync(ToModel(definition), cancellationToken: cancellationToken).ConfigureAwait(false);
                created.Add(label);
                LogCreated(definition.Collection, definition.Name);
            }
        }

        return new MongoIndexRunResult(created, dropped, unchanged);
    }

    private static async Task<List<BsonDocument>> ListAsync(IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        using var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static CreateIndexModel<BsonDocument> ToModel(MongoIndexDefinition definition)
    {
        var options = new CreateIndexOptions<BsonDocument> { Name = definition.Name, Unique = definition.Unique ? true : null };
        if (definition.PartialFilter is { } filter)
        {
            options.PartialFilterExpression = filter;
        }

        return new CreateIndexModel<BsonDocument>(new BsonDocumentIndexKeysDefinition<BsonDocument>(definition.Keys), options);
    }

    private static bool SameName(BsonDocument existing, MongoIndexDefinition definition) =>
        existing.GetValue("name", "").AsString == definition.Name;

    private static bool SameKeys(BsonDocument existing, MongoIndexDefinition definition) =>
        existing.TryGetValue("key", out var key) && KeysEqual(key.AsBsonDocument, definition.Keys);

    /// <summary>Mövcud indeks tələb olunan spesifikasiyaya tam uyğundur (açarlar, unique, partial filtr).</summary>
    internal static bool Matches(BsonDocument existing, MongoIndexDefinition definition)
    {
        if (!SameKeys(existing, definition))
        {
            return false;
        }

        var unique = existing.TryGetValue("unique", out var u) && u.ToBoolean();
        if (unique != definition.Unique)
        {
            return false;
        }

        var hasFilter = existing.TryGetValue("partialFilterExpression", out var filter);
        return definition.PartialFilter is { } expected
            ? hasFilter && filter.AsBsonDocument.Equals(expected)
            : !hasFilter;
    }

    private static bool KeysEqual(BsonDocument left, BsonDocument right)
    {
        if (left.ElementCount != right.ElementCount)
        {
            return false;
        }

        for (var i = 0; i < left.ElementCount; i++)
        {
            var (a, b) = (left.GetElement(i), right.GetElement(i));
            if (a.Name != b.Name || !ValueEqual(a.Value, b.Value))
            {
                return false;
            }
        }

        return true;
    }

    // Server açar istiqamətini int32, int64 və ya double kimi qaytara bilər.
    private static bool ValueEqual(BsonValue a, BsonValue b) =>
        a.IsNumeric && b.IsNumeric ? a.ToDouble() == b.ToDouble() : a.Equals(b);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mongo indeksi yaradıldı: {Collection}.{Index}")]
    private partial void LogCreated(string collection, string index);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ziddiyyətli Mongo indeksi atıldı: {Collection}.{Index}")]
    private partial void LogDropped(string collection, string index);
}
