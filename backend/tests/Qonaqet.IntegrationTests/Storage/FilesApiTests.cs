using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Storage;

/// <summary>Mongo + RustFS birlikdə (fayl API-ləri hər ikisini işlədir).</summary>
[CollectionDefinition(Name)]
public sealed class FilesCollection : ICollectionFixture<MongoFixture>, ICollectionFixture<RustFsFixture>
{
    public const string Name = "files";
}

/// <summary><c>files</c> kolleksiyası və yükləmə axını real Api host-u, Mongo və RustFS ilə (E03-T02).</summary>
[Collection(FilesCollection.Name)]
public sealed class FilesApiTests(MongoFixture mongo, RustFsFixture rustfs)
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\nƏ ğ ı ö ü ç ş\n"u8];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uploaded_document_is_stored_by_file_id_and_readable_only_by_its_owner()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync();
        var owner = RolesTests.Access(await host.LoginAsync("owner@example.az"));
        var other = RolesTests.Access(await host.LoginAsync("other@example.az"));

        using var upload = await UploadAsync(host, owner, Pdf, "application/pdf", "VerificationDocument");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var body = await host.JsonAsync(upload);
        var fileId = body.GetProperty("fileId").GetGuid();
        Assert.False(body.TryGetProperty("objectKey", out _));

        var doc = await host.Database.GetCollection<BsonDocument>("files").Find(new BsonDocument()).SingleAsync(Ct);
        foreach (var field in new[] { "bucket", "objectKey", "contentType", "sizeBytes", "sha256", "ownerType", "ownerId", "purpose" })
        {
            Assert.True(doc.Contains(field), field);
        }

        Assert.Equal("application/pdf", doc["contentType"].AsString);
        Assert.Equal("VerificationDocument", doc["purpose"].AsString);
        Assert.Equal(Pdf.Length, doc["sizeBytes"].ToInt64());

        using (var mine = await host.GetAsync($"/api/v1/files/{fileId}", owner))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            Assert.Equal("application/pdf", mine.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Pdf, await mine.Content.ReadAsByteArrayAsync(Ct));
        }

        using var foreign = await host.GetAsync($"/api/v1/files/{fileId}", other);
        using var anonymous = await host.GetAsync($"/api/v1/files/{fileId}", null);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
    }

    [Fact]
    public async Task Public_image_is_readable_anonymously()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync();
        var owner = RolesTests.Access(await host.LoginAsync("logo@example.az"));

        using var upload = await UploadAsync(host, owner, Png, "image/png", "Logo");
        var fileId = (await host.JsonAsync(upload)).GetProperty("fileId").GetGuid();

        using var anonymous = await host.GetAsync($"/api/v1/files/{fileId}", null);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(Png, await anonymous.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Soft_deleted_file_returns_404_but_the_object_stays_in_storage()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync();
        var owner = RolesTests.Access(await host.LoginAsync("delete@example.az"));
        using var upload = await UploadAsync(host, owner, Png, "image/png", "CatalogImage");
        var fileId = (await host.JsonAsync(upload)).GetProperty("fileId").GetGuid();

        using var deleted = await host.SendAsync(HttpMethod.Delete, $"/api/v1/files/{fileId}", owner);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var afterOwner = await host.GetAsync($"/api/v1/files/{fileId}", owner);
        using var afterAnonymous = await host.GetAsync($"/api/v1/files/{fileId}", null);
        Assert.Equal(HttpStatusCode.NotFound, afterOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterAnonymous.StatusCode);

        var doc = await host.Database.GetCollection<BsonDocument>("files").Find(new BsonDocument()).SingleAsync(Ct);
        Assert.True(doc["isDeleted"].AsBoolean);
        var storage = host.Services.GetRequiredService<IFileStorage>();
        Assert.True(await storage.ExistsAsync(doc["objectKey"].AsString, Ct));
    }

    [Fact]
    public async Task Wrong_type_and_oversized_uploads_are_rejected()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync();
        var owner = RolesTests.Access(await host.LoginAsync("bad@example.az"));

        using var exe = await UploadAsync(host, owner, [0x4D, 0x5A, 0, 1, 2], "application/pdf", "VerificationDocument");
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Equal("file_type_not_allowed", (await host.JsonAsync(exe)).GetProperty("code").GetString());

        var big = new byte[(5 * 1024 * 1024) + 10];
        Pdf.CopyTo(big, 0);
        using var tooBig = await UploadAsync(host, owner, big, "application/pdf", "VerificationDocument");
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("file_too_large", (await host.JsonAsync(tooBig)).GetProperty("code").GetString());

        Assert.Equal(0, await host.Database.GetCollection<BsonDocument>("files").CountDocumentsAsync(new BsonDocument(), cancellationToken: Ct));
    }

    [Fact]
    public async Task Upload_requires_authentication()
    {
        mongo.SkipIfUnavailable();
        rustfs.SkipIfUnavailable();
        await using var host = await NewHostAsync();

        using var response = await UploadAsync(host, null, Png, "image/png", "Logo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<ApiTestHost> NewHostAsync()
    {
        var storage = await rustfs.NewStorageAsync();
        return new ApiTestHost(mongo, configure: s =>
        {
            s.RemoveAll<IFileStorage>();
            s.AddSingleton<IFileStorage>(storage);
        });
    }

    private static async Task<HttpResponseMessage> UploadAsync(ApiTestHost host, string? accessToken, byte[] content, string contentType, string purpose)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "upload.bin");
        form.Add(new StringContent(purpose), "purpose");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/files", UriKind.Relative)) { Content = form };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await host.Client.SendAsync(request, Ct);
    }
}
