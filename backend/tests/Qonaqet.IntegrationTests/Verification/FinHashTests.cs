using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Storage;
using static Qonaqet.IntegrationTests.Verification.VerificationTestData;

namespace Qonaqet.IntegrationTests.Verification;

/// <summary>FIN hash və təkrar istifadənin qarşısının alınması (E05-T05).</summary>
[Collection(FilesCollection.Name)]
public sealed class FinHashTests(MongoFixture mongo, RustFsFixture rustfs)
{
    private const string Fin = "5abc12d";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fin_is_stored_only_as_an_hmac_of_its_upper_case_form_and_never_logged()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        var logs = new CapturingLoggerProvider();
        await using var host = await NewHostAsync(mongo, rustfs, s => s.AddSingleton<ILoggerProvider>(logs));
        var user = await host.LoginAsync("student@example.az");

        var created = await SubmitStudentAsync(host, Access(user), Fin);

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("qonaqet-dev-only::Security:FinHashSecret::not-a-secret-0123456789"), Encoding.UTF8.GetBytes(Fin.ToUpperInvariant())));
        var request = await host.Database.GetCollection<BsonDocument>("verificationRequests")
            .Find(new BsonDocument("_id", new BsonBinaryData(Guid.Parse(created.GetProperty("id").GetString()!), GuidRepresentation.Standard)))
            .SingleAsync(Ct);
        Assert.Equal(expected, request["finHash"].AsString);
        var identity = await host.Database.GetCollection<BsonDocument>("identityHashes").Find(new BsonDocument()).SingleAsync(Ct);
        Assert.Equal(expected, identity["finHash"].AsString);
        Assert.Equal(Guid.Parse(user.GetProperty("userId").GetString()!), identity["userId"].AsGuid);

        // Xam FIN heç bir kolleksiyada (outbox və audit daxil) və heç bir log sətrində yoxdur.
        foreach (var name in await (await host.Database.ListCollectionNamesAsync(cancellationToken: Ct)).ToListAsync(Ct))
        {
            var documents = await host.Database.GetCollection<BsonDocument>(name).Find(new BsonDocument()).ToListAsync(Ct);
            Assert.All(documents, d => Assert.DoesNotContain(Fin.ToUpperInvariant(), d.ToJson().ToUpperInvariant(), StringComparison.Ordinal));
        }

        Assert.NotEmpty(logs.Lines);
        Assert.All(logs.Lines, line => Assert.DoesNotContain(Fin.ToUpperInvariant(), line.ToUpperInvariant(), StringComparison.Ordinal));

        // API cavabında da hash yoxdur.
        using var mine = await host.GetAsync("/api/v1/verification/me", Access(user));
        Assert.DoesNotContain("finHash", await mine.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_fin_cannot_be_used_by_another_account_in_the_same_category()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var first = Access(await host.LoginAsync("first@example.az"));
        var second = Access(await host.LoginAsync("second@example.az"));
        await SubmitStudentAsync(host, first, Fin);

        using (var reused = await SubmitAsync(host, second, await CategoryIdAsync(host, "student"),
            new Dictionary<string, string> { ["university"] = University }, " 5ABC12D "))
        {
            Assert.Equal(System.Net.HttpStatusCode.Conflict, reused.StatusCode);
            Assert.Equal("identity_already_used", (await host.JsonAsync(reused)).GetProperty("code").GetString());
        }

        // Başqa kateqoriyada eyni FIN sərbəstdir.
        using (var other = await SubmitAsync(host, second, await CategoryIdAsync(host, "in-need"), fin: Fin))
        {
            Assert.Equal(System.Net.HttpStatusCode.Created, other.StatusCode);
        }

        Assert.Equal(2, await host.Database.GetCollection<BsonDocument>("identityHashes").CountDocumentsAsync(new BsonDocument(), cancellationToken: Ct));
    }

    [Fact]
    public async Task Unique_index_guards_fin_hash_per_category()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync(mongo, rustfs);
        var identities = host.Database.GetCollection<BsonDocument>("identityHashes");
        var category = new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard);
        BsonDocument Doc(bool deleted) => new()
        {
            { "_id", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) },
            { "finHash", "abc" },
            { "categoryId", category },
            { "isDeleted", deleted },
        };

        await identities.InsertOneAsync(Doc(deleted: true), cancellationToken: Ct);
        await identities.InsertOneAsync(Doc(deleted: false), cancellationToken: Ct);
        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() => identities.InsertOneAsync(Doc(deleted: false), cancellationToken: Ct));
        Assert.Equal(ServerErrorCategory.DuplicateKey, duplicate.WriteError.Category);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                lines.Enqueue(state.ToString() ?? string.Empty);
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                lines.Enqueue(formatter(state, exception) + " " + state + " " + exception);
            }
        }
    }
}
