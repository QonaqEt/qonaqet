using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;

namespace Qonaqet.IntegrationTests.Categories;

/// <summary>Kateqoriya testlərinin ortaq addımları.</summary>
internal static class CategoryTestData
{
    public static object StudentBody(string code = "test-students", bool isActive = true, int sortOrder = 50, object? verification = null) => new
    {
        code,
        name = "Test tələbələri",
        description = "Ali məktəb tələbələri",
        color = "#16B89B",
        sortOrder,
        isActive,
        isOpen = false,
        privacyLevel = "Public",
        verification = verification ?? new
        {
            requiresFin = true,
            minFiles = 1,
            maxFiles = 3,
            documentHint = "Tələbə arayışı",
            subtypes = Array.Empty<object>(),
            fields = new[] { new { key = "university", label = "Universitet", type = "Select", required = true, options = new[] { "BDU", "ADA Universiteti" } } },
            validity = new { type = "FixedDate", month = 9, day = 30 },
        },
        limits = new { perDay = 2, perMonth = 40, poolAccess = true, poolSpendPerDayQepik = 500 },
    };

    public static object OpenBody(string code = "test-open") => new
    {
        code,
        name = "Hamı üçün",
        color = "#FFB72B",
        sortOrder = 5,
        isActive = true,
        isOpen = true,
        privacyLevel = "Public",
        limits = new { perDay = 1, perMonth = 15, poolAccess = false, poolSpendPerDayQepik = 0 },
    };

    public static string? Access(JsonElement login) => login.GetProperty("tokens").GetProperty("accessToken").GetString();

    public static async Task<JsonElement> CreateAsync(ApiTestHost host, JsonElement admin, object body)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/categories", Access(admin), body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await host.JsonAsync(response);
    }

    /// <summary>İctimai siyahının kodları (anonim).</summary>
    public static async Task<string[]> PublicCodesAsync(ApiTestHost host)
    {
        using var response = await host.GetAsync("/api/v1/categories", null);
        response.EnsureSuccessStatusCode();
        return [.. (await host.JsonAsync(response)).EnumerateArray().Select(c => c.GetProperty("code").GetString()!)];
    }

    public static BsonBinaryData IdValue(string id) => new(Guid.Parse(id), GuidRepresentation.Standard);

    /// <summary>Kateqoriyaya istinad edən xam sənəd (məs. təsdiq sorğusu) əlavə edir.</summary>
    public static Task InsertReferenceAsync(ApiTestHost host, string collection, string categoryId, BsonDocument? extra = null)
    {
        var document = new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) },
            { "categoryId", IdValue(categoryId) },
            { "isDeleted", false },
        };
        if (extra is not null)
        {
            document.Merge(extra, overwriteExistingElements: true);
        }

        return host.Database.GetCollection<BsonDocument>(collection).InsertOneAsync(document, cancellationToken: TestContext.Current.CancellationToken);
    }
}
