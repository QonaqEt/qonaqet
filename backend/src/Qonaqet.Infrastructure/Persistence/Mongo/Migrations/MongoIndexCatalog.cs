using MongoDB.Bson;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

/// <summary>İstənilən Mongo indeksi. <see cref="Partial"/> = <c>partialFilterExpression: { isDeleted: false }</c>.</summary>
public sealed record MongoIndexDefinition(
    string Collection, string Name, BsonDocument Keys, bool Unique = false, bool Partial = true, BsonDocument? ExtraFilter = null)
{
    public static readonly BsonDocument NotDeletedFilter = new("isDeleted", false);

    /// <summary>
    /// Partial filtr: həmişə <c>isDeleted: false</c>, üstəlik (varsa) <see cref="ExtraFilter"/> şərtləri — məs. "istifadəçinin yalnız bir
    /// <c>Reserved</c> claim-i" kimi status üzrə unikallıq.
    /// </summary>
    public BsonDocument? PartialFilter => !Partial ? null : ExtraFilter is null ? NotDeletedFilter : new BsonDocument(NotDeletedFilter).AddRange(ExtraFilter);
}

/// <summary>
/// <c>CLAUDE.md</c> → "Indexing rules": unikal və sorğu indeksləri partial-dır (<c>isDeleted: false</c>),
/// bərabərlik sahələri əvvəldə. Hər soft-deletable kolleksiyaya admin "include deleted" siyahıları üçün
/// bir qeyri-partial (<c>isDeleted</c>, <c>deletedAt</c>) indeksi əlavə olunur. TTL indeksi yoxdur.
/// </summary>
public static class MongoIndexCatalog
{
    public const string AdminIndexName = "ix_admin_isDeleted_deletedAt";

    /// <summary>Soft-deletable kolleksiyalar (backfill + admin indeksi).</summary>
    public static readonly IReadOnlyList<string> SoftDeletableCollections =
    [
        "users", "otpCodes", "refreshTokens", "files", "categories", "identityHashes", "verificationRequests",
        "businesses", "businessMemberships", "catalogItems", "offers", "pools", "poolDonations", "campaigns", "claims", "gifts",
        "corporates", "corporateGiftOrders", "brands", "coupons", "couponCodes", "reports",
    ];

    /// <summary>Append-only kolleksiyalar: soft delete olunmur, amma <c>isDeleted</c> sahəsi yenə də var.</summary>
    public static readonly IReadOnlyList<string> AppendOnlyCollections = ["giftRedemptions"];

    public static IReadOnlyList<MongoIndexDefinition> All { get; } = Build();

    private static List<MongoIndexDefinition> Build()
    {
        var list = new List<MongoIndexDefinition>
        {
            Unique("users", ("email", 1)),
            Unique("categories", ("code", 1)),
            Unique("identityHashes", ("finHash", 1), ("categoryId", 1)),
            Unique("businesses", ("voen", 1)),
            Unique("businesses", ("slug", 1)),
            Unique("businessMemberships", ("businessId", 1), ("userId", 1)),
            Unique("couponCodes", ("couponId", 1), ("code", 1)),
            Unique("gifts", ("linkTokenHash", 1)),

            // Kateqoriyanın bir default (campaignId: null) və hər kampaniyanın bir hovuzu.
            Unique("pools", ("categoryId", 1), ("campaignId", 1)),

            Query("offers", ("businessId", 1), ("type", 1)),
            Query("offers", ("categoryId", 1), ("type", 1)),
            Geo("businesses", "location"),
            Query("businesses", ("status", 1), ("acceptsPool", 1)),
            Query("businesses", ("status", 1), ("acceptsNetworkVouchers", 1)),
            Query("businessMemberships", ("userId", 1), ("createdAt", 1)),
            Query("categories", ("isActive", 1), ("sortOrder", 1)),
            Query("users", ("verifiedCategories.categoryId", 1)),
            Query("users", ("verifiedCategories.validUntil", 1)),
            Query("campaigns", ("categoryId", 1), ("status", 1)),
            Query("catalogItems", ("businessId", 1), ("isActive", 1)),
            Query("claims", ("recipientUserId", 1), ("status", 1), ("createdAt", 1)),
            Query("claims", ("recipientUserId", 1), ("offerId", 1), ("createdAt", 1)),

            // Bir istifadəçinin eyni anda yalnız bir aktiv (Reserved) claim-i: paralel claim-lərdə də (E06-T03).
            new("claims", "ux_claims_recipientUserId_reserved", new BsonDocument("recipientUserId", 1), Unique: true,
                ExtraFilter: new BsonDocument("status", "Reserved")),
            Query("claims", ("businessId", 1), ("status", 1)),
            Query("claims", ("status", 1), ("expiresAt", 1)),
            Query("gifts", ("recipientUserId", 1), ("status", 1)),
            Query("gifts", ("recipientEmail", 1), ("status", 1)),
            Query("gifts", ("senderUserId", 1), ("createdAt", 1)),
            Query("gifts", ("businessId", 1), ("status", 1)),
            Query("gifts", ("corporateOrderId", 1)),
            Query("gifts", ("status", 1), ("deliverAt", 1)),
            Query("gifts", ("status", 1), ("expiresAt", 1)),
            Query("verificationRequests", ("status", 1), ("createdAt", 1)),
            Query("verificationRequests", ("userId", 1), ("categoryId", 1)),
            Query("giftRedemptions", ("giftId", 1)),
            Query("giftRedemptions", ("businessId", 1), ("createdAt", 1)),
            Query("otpCodes", ("email", 1), ("createdAt", 1)),
            Query("refreshTokens", ("tokenHash", 1)),
            Query("refreshTokens", ("userId", 1), ("revokedAt", 1)),
            Query("files", ("ownerType", 1), ("ownerId", 1)),

            // Outbox relay-in növbəsi (append-only, isDeleted yoxdur → partial deyil).
            new(Messaging.Outbox.MongoOutbox.CollectionName, "ix_outboxMessages_processedAt_occurredAt",
                new BsonDocument { { "processedAt", 1 }, { "occurredAt", 1 } }, Partial: false),
        };

        list.AddRange(SoftDeletableCollections.Select(c =>
            new MongoIndexDefinition(c, AdminIndexName, new BsonDocument { { "isDeleted", 1 }, { "deletedAt", 1 } }, Partial: false)));

        return list;
    }

    private static MongoIndexDefinition Unique(string collection, params (string Field, int Order)[] fields) =>
        new(collection, Name("ux", collection, fields.Select(f => f.Field)), Keys(fields), Unique: true);

    private static MongoIndexDefinition Query(string collection, params (string Field, int Order)[] fields) =>
        new(collection, Name("ix", collection, fields.Select(f => f.Field)), Keys(fields));

    private static MongoIndexDefinition Geo(string collection, string field) =>
        new(collection, Name("geo", collection, [field]), new BsonDocument(field, "2dsphere"));

    private static string Name(string prefix, string collection, IEnumerable<string> fields) =>
        $"{prefix}_{collection}_{string.Join('_', fields)}";

    private static BsonDocument Keys((string Field, int Order)[] fields)
    {
        var keys = new BsonDocument();
        foreach (var (field, order) in fields)
        {
            keys.Add(field, order);
        }

        return keys;
    }
}
