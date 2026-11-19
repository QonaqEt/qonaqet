using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Categories.CategoryTestData;

namespace Qonaqet.IntegrationTests.Categories;

/// <summary>Kateqoriya aqreqatı və admin CRUD (E05-T01).</summary>
[Collection(MongoCollection.Name)]
public sealed class CategoryCrudTests(MongoFixture mongo)
{
    [Fact]
    public async Task Admin_created_category_appears_in_the_public_list_without_code_changes()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");

        var created = await CreateAsync(host, admin, StudentBody());
        Assert.Equal("test-students", created.GetProperty("code").GetString());
        Assert.Equal("#16B89B", created.GetProperty("color").GetString());

        using var list = await host.GetAsync("/api/v1/categories", null);
        var category = (await host.JsonAsync(list)).EnumerateArray().Single(c => c.GetProperty("code").GetString() == "test-students");
        var verification = category.GetProperty("verification");
        Assert.True(verification.GetProperty("requiresFin").GetBoolean());
        Assert.Equal("FixedDate", verification.GetProperty("validity").GetProperty("type").GetString());
        var field = verification.GetProperty("fields").EnumerateArray().Single();
        Assert.Equal("Select", field.GetProperty("type").GetString());
        Assert.Equal(["BDU", "ADA Universiteti"], field.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());
        Assert.Equal(40, category.GetProperty("limits").GetProperty("perMonth").GetInt32());

        Assert.True(await Businesses.BusinessTestData.AuditExistsAsync(host, "CategoryCreate"));
    }

    [Fact]
    public async Task Only_admins_manage_categories()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var user = await host.LoginAsync("user@example.az");

        using var create = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/categories", Access(user), StudentBody());
        using var anonymous = await host.SendAsync(HttpMethod.Get, "/api/v1/admin/categories", null);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Deactivated_category_leaves_the_public_list_but_stays_for_admins()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString();
        await CreateAsync(host, admin, OpenBody());

        using (var update = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(isActive: false)))
        {
            Assert.False((await host.JsonAsync(update)).GetProperty("isActive").GetBoolean());
        }

        var codes = await PublicCodesAsync(host);
        Assert.Contains("test-open", codes);
        Assert.DoesNotContain("test-students", codes);

        using var adminList = await host.GetAsync("/api/v1/admin/categories", Access(admin));
        var items = (await host.JsonAsync(adminList)).GetProperty("items").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToArray();
        Assert.Contains("test-students", items);
    }

    [Fact]
    public async Task Invalid_configuration_is_rejected_field_by_field()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");

        var badVerification = new
        {
            requiresFin = false,
            minFiles = 3,
            maxFiles = 1,
            fields = new object[]
            {
                new { key = "university", label = "Universitet", type = "Select", required = true, options = Array.Empty<string>() },
            },
            validity = new { type = "Months", months = 0 },
        };
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/categories", Access(admin),
            new
            {
                code = "Bad Code",
                name = "X",
                color = "green",
                sortOrder = 1,
                isActive = true,
                isOpen = false,
                privacyLevel = "Public",
                verification = badVerification,
                limits = new { perDay = 5, perMonth = 1, poolAccess = false, poolSpendPerDayQepik = 0 },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var code in new[] { "category_code_invalid", "color_invalid", "limits_invalid", "files_range_invalid", "fields_invalid", "validity_invalid" })
        {
            Assert.Contains(code, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Code_is_unique_among_live_categories_and_frozen_once_in_use()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");

        // Silinmiş sənədin kodu partial unikal indeksə mane olmur.
        await host.Database.GetCollection<BsonDocument>("categories").InsertOneAsync(
            new BsonDocument { { "_id", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) }, { "code", "test-students" }, { "isDeleted", true } },
            cancellationToken: TestContext.Current.CancellationToken);
        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString()!;
        await CreateAsync(host, admin, OpenBody());

        using (var duplicate = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/categories", Access(admin), StudentBody()))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("category_code_taken", (await host.JsonAsync(duplicate)).GetProperty("code").GetString());
        }

        using (var taken = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(code: "test-open")))
        {
            Assert.Equal("category_code_taken", (await host.JsonAsync(taken)).GetProperty("code").GetString());
        }

        // İstifadədə deyil: kod dəyişir.
        using (var rename = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(code: "test-university")))
        {
            Assert.Equal("test-university", (await host.JsonAsync(rename)).GetProperty("code").GetString());
        }

        await InsertReferenceAsync(host, "verificationRequests", id);

        using (var frozen = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(code: "test-renamed")))
        {
            Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
            Assert.Equal("category_code_in_use", (await host.JsonAsync(frozen)).GetProperty("code").GetString());
        }

        // Kodu dəyişməyən redaktə istifadədə olan kateqoriyada da mümkündür.
        using var edit = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(code: "test-university", sortOrder: 7));
        Assert.Equal(7, (await host.JsonAsync(edit)).GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task Icon_must_be_an_existing_logo_file()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");

        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/categories", Access(admin),
            new
            {
                code = "test-icon",
                name = "İkon",
                iconFileId = Guid.CreateVersion7(),
                color = "#2A1A3E",
                sortOrder = 1,
                isActive = true,
                isOpen = true,
                privacyLevel = "Sensitive",
                limits = new { perDay = 1, perMonth = 1, poolAccess = false, poolSpendPerDayQepik = 0 },
            });

        Assert.Equal("category_icon_invalid", (await host.JsonAsync(response)).GetProperty("code").GetString());
    }
}
