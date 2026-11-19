using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Storage;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Verification.VerificationTestData;

namespace Qonaqet.IntegrationTests.Verification;

/// <summary>Admin təsdiq/rədd və etibarlılıq müddəti (E05-T06).</summary>
[Collection(FilesCollection.Name)]
public sealed class VerificationReviewTests(MongoFixture mongo, RustFsFixture rustfs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approval_writes_verified_category_with_validity_from_the_category_rule()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "admin@example.az"));
        var login = await host.LoginAsync("student@example.az");
        var user = Access(login);
        var created = await SubmitStudentAsync(host, user);
        var id = created.GetProperty("id").GetString();

        using (var queue = await host.GetAsync("/api/v1/admin/verifications?status=Pending", admin))
        {
            var item = (await host.JsonAsync(queue)).GetProperty("items").EnumerateArray().Single();
            Assert.Equal("student@example.az", item.GetProperty("userEmail").GetString());
            Assert.True(item.GetProperty("hasFin").GetBoolean());
            Assert.Single(item.GetProperty("fileIds").EnumerateArray());
            Assert.Equal(University, item.GetProperty("request").GetProperty("fields").GetProperty("university").GetString());
        }

        // Admin sənədi oxuya bilir.
        using (var details = await host.GetAsync($"/api/v1/admin/verifications/{id}", admin))
        {
            var fileId = (await host.JsonAsync(details)).GetProperty("fileIds").EnumerateArray().Single().GetString();
            using var file = await host.GetAsync($"/api/v1/files/{fileId}", admin);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        }

        using (var approve = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/approve", admin, new { }))
        {
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.Equal("Approved", (await host.JsonAsync(approve)).GetProperty("status").GetString());
        }

        var expected = BakuTime.EndOfDayUtc(NextSeptember30(host.Clock.UtcNow));
        using (var mine = await host.GetAsync("/api/v1/verification/me", user))
        {
            var body = await host.JsonAsync(mine);
            var verified = body.GetProperty("verified").EnumerateArray().Single();
            Assert.Equal("student", verified.GetProperty("categoryCode").GetString());
            Assert.True(verified.GetProperty("isValid").GetBoolean());
            Assert.Equal(expected, verified.GetProperty("validUntil").GetDateTimeOffset());
            Assert.Equal("Approved", body.GetProperty("requests").EnumerateArray().Single().GetProperty("status").GetString());
        }

        var stored = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument("email", "student@example.az")).SingleAsync(Ct);
        var entry = stored["verifiedCategories"].AsBsonArray.Single().AsBsonDocument;
        Assert.Equal(Guid.Parse(id!), entry["verificationId"].AsGuid);
        Assert.True(await Businesses.BusinessTestData.AuditExistsAsync(host, "VerificationApprove"));

        using var twice = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/approve", admin, new { });
        Assert.Equal("verification_already_reviewed", (await host.JsonAsync(twice)).GetProperty("code").GetString());

        // Etibarlı təsdiq varkən (14 gündən çox qalıb) yeni sorğu qəbul olunmur.
        using var renew = await SubmitAsync(host, user, await CategoryIdAsync(host, "student"), new Dictionary<string, string> { ["university"] = University }, "5ABC12D");
        Assert.Equal("already_verified", (await host.JsonAsync(renew)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_can_set_and_change_valid_until_and_expired_verification_stops_counting()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "admin@example.az"));
        var user = Access(await host.LoginAsync("veteran@example.az"));
        using var submit = await SubmitAsync(host, user, await CategoryIdAsync(host, "veteran-family"), fin: "7XYZ890", subtype: "qazi");
        var id = (await host.JsonAsync(submit)).GetProperty("id").GetString();

        using (var past = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/approve", admin, new { validUntil = host.Clock.UtcNow.AddDays(-1) }))
        {
            Assert.Equal("valid_until_invalid", (await host.JsonAsync(past)).GetProperty("code").GetString());
        }

        var manual = host.Clock.UtcNow.AddDays(30);
        using (var approve = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/approve", admin, new { validUntil = manual }))
        {
            Assert.Equal(manual, (await host.JsonAsync(approve)).GetProperty("validUntil").GetDateTimeOffset());
        }

        var shortened = host.Clock.UtcNow.AddDays(2);
        using (var change = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/verifications/{id}/valid-until", admin, new { validUntil = shortened }))
        {
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }

        Assert.Equal(shortened, await VerifiedUntilAsync(host, user));
        Assert.True(await Businesses.BusinessTestData.AuditExistsAsync(host, "VerificationChangeValidity"));

        // Müddət keçəndə təsdiq avtomatik qüvvədən düşür (məntiqi müddət) və kateqoriyanın silinməsinə mane olmur.
        host.Clock.UtcNow = host.Clock.UtcNow.AddDays(3);
        using (var mine = await host.GetAsync("/api/v1/verification/me", user))
        {
            Assert.False((await host.JsonAsync(mine)).GetProperty("verified").EnumerateArray().Single().GetProperty("isValid").GetBoolean());
        }
    }

    [Fact]
    public async Task Rejection_records_the_reason_and_frees_the_fin()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "admin@example.az"));
        var first = Access(await host.LoginAsync("first@example.az"));
        var second = Access(await host.LoginAsync("second@example.az"));
        var id = (await SubmitStudentAsync(host, first)).GetProperty("id").GetString();

        using (var noReason = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/reject", admin, new { reason = "" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        }

        using (var reject = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/reject", admin, new { reason = "Arayış oxunmur" }))
        {
            var body = await host.JsonAsync(reject);
            Assert.Equal("Rejected", body.GetProperty("status").GetString());
            Assert.Equal("Arayış oxunmur", body.GetProperty("rejectReason").GetString());
        }

        using (var mine = await host.GetAsync("/api/v1/verification/me", first))
        {
            var body = await host.JsonAsync(mine);
            Assert.Empty(body.GetProperty("verified").EnumerateArray());
            Assert.Equal("Arayış oxunmur", body.GetProperty("requests").EnumerateArray().Single().GetProperty("rejectReason").GetString());
        }

        // FIN bağı açıldı: düzgün sahib başqa hesabla göndərə bilər (sonra admin qərar verir).
        await SubmitStudentAsync(host, second);

        using var nonAdmin = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/verifications/{id}/approve", first, new { });
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
    }

    private static DateOnly NextSeptember30(DateTimeOffset now)
    {
        var today = BakuTime.ToBakuDate(now);
        var candidate = new DateOnly(today.Year, 9, 30);
        return candidate < today ? candidate.AddYears(1) : candidate;
    }

    private static async Task<DateTimeOffset?> VerifiedUntilAsync(ApiTestHost host, string? user)
    {
        using var mine = await host.GetAsync("/api/v1/verification/me", user);
        return (await host.JsonAsync(mine)).GetProperty("verified").EnumerateArray().Single().GetProperty("validUntil").GetDateTimeOffset();
    }
}
