using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Storage;

namespace Qonaqet.IntegrationTests.Verification;

/// <summary>Təsdiq testlərinin ortaq addımları: seed kateqoriyaları, multipart sorğu.</summary>
internal static class VerificationTestData
{
    public static readonly byte[] Pdf = [.. "%PDF-1.7\nTələbə arayışı\n"u8];
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    public const string University = "Bakı Dövlət Universiteti";

    public static async Task<ApiTestHost> NewHostAsync(MongoFixture mongo, RustFsFixture rustfs, Action<IServiceCollection>? configure = null)
    {
        var storage = await rustfs.NewStorageAsync();
        return new ApiTestHost(mongo, configure: s =>
        {
            s.RemoveAll<IFileStorage>();
            s.AddSingleton<IFileStorage>(storage);
            configure?.Invoke(s);
        });
    }

    public static string? Access(JsonElement login) => login.GetProperty("tokens").GetProperty("accessToken").GetString();

    /// <summary>Seed kateqoriyasının ID-si (kod üzrə, ictimai siyahıdan).</summary>
    public static async Task<string> CategoryIdAsync(ApiTestHost host, string code)
    {
        using var response = await host.GetAsync("/api/v1/categories", null);
        return (await host.JsonAsync(response)).EnumerateArray().Single(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetString()!;
    }

    public static async Task<HttpResponseMessage> SubmitAsync(
        ApiTestHost host, string? accessToken, string categoryId, IDictionary<string, string>? fields = null, string? fin = null, string? subtype = null,
        IEnumerable<(byte[] Content, string ContentType)>? files = null)
    {
        using var form = new MultipartFormDataContent { { new StringContent(categoryId), "categoryId" } };
        if (fields is not null)
        {
            form.Add(new StringContent(JsonSerializer.Serialize(fields)), "fields");
        }

        if (fin is not null)
        {
            form.Add(new StringContent(fin), "fin");
        }

        if (subtype is not null)
        {
            form.Add(new StringContent(subtype), "subtype");
        }

        var index = 0;
        foreach (var (content, contentType) in files ?? [(Pdf, "application/pdf")])
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, "files", $"doc{index++}.bin");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/verification", UriKind.Relative)) { Content = form };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Tələbə kateqoriyası üçün düzgün sorğu; cavabın JSON-u.</summary>
    public static async Task<JsonElement> SubmitStudentAsync(ApiTestHost host, string? accessToken, string fin = "5abc12d")
    {
        var categoryId = await CategoryIdAsync(host, "student");
        using var response = await SubmitAsync(host, accessToken, categoryId, new Dictionary<string, string> { ["university"] = University }, fin);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await host.JsonAsync(response);
    }
}
