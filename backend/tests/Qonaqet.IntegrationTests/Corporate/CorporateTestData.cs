using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Businesses;
using Qonaqet.IntegrationTests.Identity;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Korporativ testlərin ortaq addımları: şirkət, şirkət istifadəçisi, sifariş, CSV.</summary>
internal static class CorporateTestData
{
    /// <summary>Test host-u üçün yaddaşda fayl saxlaması (RustFS-siz) və saxta PDF renderer.</summary>
    public static void Services(IServiceCollection services)
    {
        services.RemoveAll<IFileStorage>();
        services.AddSingleton<IFileStorage, InMemoryObjectStorage>();
        services.RemoveAll<IPdfRenderer>();
        services.AddSingleton<IPdfRenderer, FakePdfRenderer>();
    }

    /// <summary>Admin şirkət yaradır və istifadəçini ona bağlayır; şirkət istifadəçisinin rolu olan təzə girişi.</summary>
    public static async Task<CorporateSetup> CorporateAsync(ApiTestHost host, string prefix, string name = "Azercell")
    {
        var admin = await RolesTests.LoginAsAdminAsync(host, $"{prefix}-admin@example.az");
        var voen = string.Concat(Enumerable.Range(0, 10).Select(_ => Random.Shared.Next(10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var create = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/corporates", Access(admin),
            new { name, voen, brandColor = "#2A1A3E", contactEmail = $"hr@{prefix}.az" });
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var corporateId = (await host.JsonAsync(create)).GetProperty("id").GetString()!;

        var hr = await host.LoginAsync($"{prefix}-hr@example.az");
        using (var assign = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporates/{corporateId}/users", Access(admin), new { email = $"{prefix}-hr@example.az" }))
        {
            Assert.True(assign.IsSuccessStatusCode, await assign.Content.ReadAsStringAsync(Ct));
        }

        return new CorporateSetup(admin, await BusinessTestData.RefreshAsync(host, hr), corporateId, voen);
    }

    public static async Task<JsonElement> OrderAsync(ApiTestHost host, JsonElement hr, object body)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/corporate/orders", Access(hr), body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    public static async Task<HttpResponseMessage> UploadAsync(ApiTestHost host, JsonElement hr, string orderId, string csv)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "recipients.csv");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/corporate/orders/{orderId}/recipients", UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Access(hr));
        return await host.Client.SendAsync(request, Ct);
    }

    public static string Csv(int count, string domain = "staff.az") =>
        "name,email\n" + string.Join('\n', Enumerable.Range(1, count).Select(i => $"İşçi {i},user{i}@{domain}"));

    public static Task<BsonDocument> OrderDocAsync(ApiTestHost host, string orderId) =>
        host.Database.GetCollection<BsonDocument>("corporateGiftOrders")
            .Find(new BsonDocument("_id", new BsonBinaryData(Guid.Parse(orderId), GuidRepresentation.Standard))).SingleAsync(Ct);

    public static Task<List<BsonDocument>> GiftsOfAsync(ApiTestHost host, string orderId) =>
        host.Database.GetCollection<BsonDocument>("gifts")
            .Find(new BsonDocument("corporateOrderId", new BsonBinaryData(Guid.Parse(orderId), GuidRepresentation.Standard))).ToListAsync(Ct);
}

internal sealed record CorporateSetup(JsonElement Admin, JsonElement Hr, string CorporateId, string Voen);

/// <summary>Yaddaşda S3-ə bənzər saxlama (dəyişməz açar, silmə yoxdur).</summary>
internal sealed class InMemoryObjectStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);

    public string Bucket => "in-memory";

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (!_objects.TryAdd(objectKey, (buffer.ToArray(), contentType)))
        {
            throw new InvalidOperationException("Açar artıq var.");
        }
    }

    public Task<StoredObject?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.TryGetValue(objectKey, out var o)
            ? new StoredObject(new MemoryStream(o.Bytes, writable: false), o.ContentType, o.Bytes.LongLength)
            : null);

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) => Task.FromResult(_objects.ContainsKey(objectKey));
}

/// <summary>Chromium-suz PDF: şablon modeli və cədvəl mətndə saxlanılır ki, testlər məzmunu yoxlasın.</summary>
internal sealed class FakePdfRenderer : IPdfRenderer
{
    public ConcurrentQueue<string> Rendered { get; } = new();

    public Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken) => Task.FromResult(Pdf(html));

    public Task<byte[]> RenderTemplateAsync(string templateName, IReadOnlyDictionary<string, string?> model, PdfTable? table, CancellationToken cancellationToken)
    {
        var text = string.Join('\n', model.Select(kv => $"{kv.Key}={kv.Value}"))
            + "\n" + string.Join('\n', (table?.Rows ?? []).Select(r => string.Join('|', r)));
        Rendered.Enqueue(text);
        return Task.FromResult(Pdf(text));
    }

    private static byte[] Pdf(string text) => Encoding.UTF8.GetBytes("%PDF-1.4\n" + text + "\n%%EOF");
}
