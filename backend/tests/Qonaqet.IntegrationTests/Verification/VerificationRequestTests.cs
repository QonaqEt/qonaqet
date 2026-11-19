using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Storage;
using static Qonaqet.IntegrationTests.Verification.VerificationTestData;

namespace Qonaqet.IntegrationTests.Verification;

/// <summary>Təsdiq sorğusu API-si — dinamik sihirbaz (E05-T04).</summary>
[Collection(FilesCollection.Name)]
public sealed class VerificationRequestTests(MongoFixture mongo, RustFsFixture rustfs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Student_submits_a_request_and_sees_its_status()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var user = Access(await host.LoginAsync("tələbə@example.az"));
        var categoryId = await CategoryIdAsync(host, "student");

        using var response = await SubmitAsync(host, user, categoryId, new Dictionary<string, string> { ["university"] = University }, "5abc12d",
            files: [(Pdf, "application/pdf"), (Png, "image/png")]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await host.JsonAsync(response);
        Assert.Equal("Pending", created.GetProperty("status").GetString());
        Assert.Equal(2, created.GetProperty("fileCount").GetInt32());
        Assert.Equal(University, created.GetProperty("fields").GetProperty("university").GetString());
        Assert.False(created.TryGetProperty("finHash", out _));

        // Sənədlər sorğunun sahibliyi ilə, təsdiq təyinatı ilə saxlanılıb.
        var requestId = Guid.Parse(created.GetProperty("id").GetString()!);
        var files = await host.Database.GetCollection<BsonDocument>("files").Find(new BsonDocument("ownerId", new BsonBinaryData(requestId, GuidRepresentation.Standard)))
            .ToListAsync(Ct);
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.Equal("VerificationDocument", f["purpose"].AsString));
        Assert.All(files, f => Assert.Equal("VerificationRequest", f["ownerType"].AsString));

        using var mine = await host.GetAsync("/api/v1/verification/me", user);
        var request = (await host.JsonAsync(mine)).GetProperty("requests").EnumerateArray().Single();
        Assert.Equal("student", request.GetProperty("categoryCode").GetString());
        Assert.Equal("Pending", request.GetProperty("status").GetString());

        // Eyni kateqoriyada ikinci baxılmamış sorğu olmur.
        using var again = await SubmitAsync(host, user, categoryId, new Dictionary<string, string> { ["university"] = University }, "5abc12d");
        Assert.Equal("verification_pending", (await host.JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_is_built_from_the_category_document()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var user = Access(await host.LoginAsync("user@example.az"));
        var student = await CategoryIdAsync(host, "student");
        var veteran = await CategoryIdAsync(host, "veteran-family");

        using (var missing = await SubmitAsync(host, user, student, fields: null, fin: "12", files: []))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            var body = await missing.Content.ReadAsStringAsync(Ct);
            Assert.Contains("field_required", body, StringComparison.Ordinal);
            Assert.Contains("fin_invalid", body, StringComparison.Ordinal);
            Assert.Contains("files_count_invalid", body, StringComparison.Ordinal);
        }

        using (var unknownOption = await SubmitAsync(host, user, student, new Dictionary<string, string> { ["university"] = "Harvard", ["hobby"] = "şahmat" }, "5ABC12D"))
        {
            var body = await unknownOption.Content.ReadAsStringAsync(Ct);
            Assert.Contains("field_invalid", body, StringComparison.Ordinal);
            Assert.Contains("field_unknown", body, StringComparison.Ordinal);
        }

        using (var noSubtype = await SubmitAsync(host, user, veteran, fin: "5ABC12D"))
        {
            Assert.Contains("subtype_invalid", await noSubtype.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        using (var subtype = await SubmitAsync(host, user, veteran, fin: "5ABC12D", subtype: "martyr-family"))
        {
            Assert.Equal(HttpStatusCode.Created, subtype.StatusCode);
        }

        using (var open = await SubmitAsync(host, user, await CategoryIdAsync(host, "everyone")))
        {
            Assert.Equal("category_verification_not_required", (await host.JsonAsync(open)).GetProperty("code").GetString());
        }

        using var unknown = await SubmitAsync(host, user, Guid.CreateVersion7().ToString());
        Assert.Equal("category_not_found", (await host.JsonAsync(unknown)).GetProperty("code").GetString());

        Assert.Equal(1, await host.Database.GetCollection<BsonDocument>("verificationRequests").CountDocumentsAsync(new BsonDocument(), cancellationToken: Ct));
    }

    [Fact]
    public async Task Documents_must_be_pdf_jpg_or_png_up_to_five_megabytes_and_nothing_is_stored_otherwise()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var user = Access(await host.LoginAsync("user@example.az"));
        var student = await CategoryIdAsync(host, "student");
        var fields = new Dictionary<string, string> { ["university"] = University };

        byte[] large = [.. Pdf, .. new byte[(5 * 1024 * 1024) + 1 - Pdf.Length]];
        using (var tooLarge = await SubmitAsync(host, user, student, fields, "5ABC12D", files: [(Pdf, "application/pdf"), (large, "application/pdf")]))
        {
            Assert.Equal("file_too_large", (await host.JsonAsync(tooLarge)).GetProperty("code").GetString());
        }

        using (var gif = await SubmitAsync(host, user, student, fields, "5ABC12D", files: [([.. "GIF89a"u8, 1, 2], "image/gif")]))
        {
            Assert.Equal("file_type_not_allowed", (await host.JsonAsync(gif)).GetProperty("code").GetString());
        }

        Assert.Equal(0, await host.Database.GetCollection<BsonDocument>("files").CountDocumentsAsync(new BsonDocument(), cancellationToken: Ct));
        Assert.Equal(0, await host.Database.GetCollection<BsonDocument>("verificationRequests").CountDocumentsAsync(new BsonDocument(), cancellationToken: Ct));
    }

    [Fact]
    public async Task Requests_are_private_to_their_owner()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var owner = Access(await host.LoginAsync("owner@example.az"));
        var other = Access(await host.LoginAsync("other@example.az"));
        await SubmitStudentAsync(host, owner);

        using var foreign = await host.GetAsync("/api/v1/verification/me", other);
        Assert.Empty((await host.JsonAsync(foreign)).GetProperty("requests").EnumerateArray());

        using var anonymous = await host.GetAsync("/api/v1/verification/me", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
