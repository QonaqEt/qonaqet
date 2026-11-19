using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Verification.Jobs;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Verification;

/// <summary>Etibarlılıq müddəti xatırlatmaları (E05-T07).</summary>
[Collection(MongoCollection.Name)]
public sealed class VerificationReminderTests(MongoFixture mongo)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reminder_goes_once_per_validity_period_fourteen_days_before_expiry()
    {
        mongo.SkipIfUnavailable();
        var emails = new CapturingEmailQueue();
        await using var host = NewHost(emails);
        await host.LoginAsync("student@example.az");
        var now = host.Clock.UtcNow;
        var student = await VerificationTestData.CategoryIdAsync(host, "student");
        var veteran = await VerificationTestData.CategoryIdAsync(host, "veteran-family");
        var inNeed = await VerificationTestData.CategoryIdAsync(host, "in-need");
        await SetVerifiedAsync(host, "student@example.az",
            Entry(student, now.AddDays(10)),   // xatırlatma vaxtıdır
            Entry(veteran, now.AddDays(30)),   // hələ tezdir
            Entry(inNeed, now.AddDays(-1)));   // müddəti bitib: qüvvədə deyil, xatırladılmır

        Assert.Equal(1, await RunAsync(host));
        var email = Assert.Single(emails.Messages);
        Assert.Equal("student@example.az", email.To);
        Assert.Contains("Tələbələr", email.Subject, StringComparison.Ordinal);
        Assert.Contains("Tələbələr", email.HtmlBody, StringComparison.Ordinal);

        // Eyni dövr üçün təkrar yoxdur.
        Assert.Equal(0, await RunAsync(host));
        Assert.Single(emails.Messages);

        // Admin müddəti dəyişdi (yeni dövr) → yenidən bir dəfə xatırladılır.
        await host.Database.GetCollection<BsonDocument>("users").UpdateOneAsync(
            new BsonDocument { { "email", "student@example.az" }, { "verifiedCategories.categoryId", Id(student) } },
            new BsonDocument("$set", new BsonDocument("verifiedCategories.$.validUntil", now.AddDays(12).UtcDateTime)),
            cancellationToken: Ct);
        Assert.Equal(1, await RunAsync(host));
        Assert.Equal(2, emails.Messages.Count);

        // 16 gün sonra: veteran təsdiqi pəncərəyə girir, tələbə təsdiqi artıq bitib.
        host.Clock.UtcNow = now.AddDays(16);
        Assert.Equal(1, await RunAsync(host));
        Assert.Contains("Qazilər", emails.Messages[^1].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_respects_the_notification_preference_but_the_period_is_still_marked()
    {
        mongo.SkipIfUnavailable();
        var emails = new CapturingEmailQueue();
        await using var host = NewHost(emails);
        var login = await host.LoginAsync("quiet@example.az");
        using (var optOut = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/notifications", VerificationTestData.Access(login),
            new { items = new[] { new { type = "Verification", push = true, email = false } } }))
        {
            Assert.True(optOut.IsSuccessStatusCode, await optOut.Content.ReadAsStringAsync(Ct));
        }

        var student = await VerificationTestData.CategoryIdAsync(host, "student");
        await SetVerifiedAsync(host, "quiet@example.az", Entry(student, host.Clock.UtcNow.AddDays(5)));

        Assert.Equal(1, await RunAsync(host));
        Assert.Empty(emails.Messages);
        Assert.Equal(0, await RunAsync(host));
    }

    private ApiTestHost NewHost(CapturingEmailQueue emails) => new(mongo, configure: s =>
    {
        s.RemoveAll<IEmailQueue>();
        s.AddSingleton<IEmailQueue>(emails);
        s.AddScoped<VerificationRemindersJob>();
    });

    private static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VerificationRemindersJob>().RunAsync(Ct);
    }

    private static BsonBinaryData Id(string id) => new(Guid.Parse(id), GuidRepresentation.Standard);

    private static BsonDocument Entry(string categoryId, DateTimeOffset validUntil) => new()
    {
        { "categoryId", Id(categoryId) },
        { "validUntil", validUntil.UtcDateTime },
        { "verificationId", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) },
        { "verifiedAt", DateTime.UtcNow },
    };

    private static Task SetVerifiedAsync(ApiTestHost host, string email, params BsonDocument[] entries) =>
        host.Database.GetCollection<BsonDocument>("users").UpdateOneAsync(
            new BsonDocument("email", email), new BsonDocument("$set", new BsonDocument("verifiedCategories", new BsonArray(entries))), cancellationToken: Ct);
}
