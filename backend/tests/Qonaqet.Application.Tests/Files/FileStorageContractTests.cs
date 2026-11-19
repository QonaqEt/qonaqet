using System.Text;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Tests.Fakes;

namespace Qonaqet.Application.Tests.Files;

/// <summary>Vahid testlərin işlətdiyi <see cref="InMemoryFileStorage"/> S3 qaydalarını təkrarlayır.</summary>
public sealed class FileStorageContractTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Objects_round_trip_and_keys_are_immutable()
    {
        var storage = new InMemoryFileStorage();
        var key = ObjectKeys.New("CatalogImage", DateTimeOffset.Parse("2026-11-19T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        await using (var body = new MemoryStream(Encoding.UTF8.GetBytes("ilk")))
        {
            await storage.PutAsync(key, body, "image/png", Ct);
        }

        await using var again = new MemoryStream(Encoding.UTF8.GetBytes("ikinci"));
        await Assert.ThrowsAsync<ObjectAlreadyExistsException>(() => storage.PutAsync(key, again, "image/png", Ct));

        await using var stored = await storage.OpenReadAsync(key, Ct);
        Assert.NotNull(stored);
        using var reader = new StreamReader(stored.Content);
        Assert.Equal("ilk", await reader.ReadToEndAsync(Ct));
        Assert.Equal("image/png", stored.ContentType);
    }

    [Fact]
    public void New_keys_are_unique_and_grouped_by_purpose_and_month()
    {
        var now = DateTimeOffset.Parse("2026-11-19T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var a = ObjectKeys.New("VerificationDocument", now);
        var b = ObjectKeys.New("VerificationDocument", now);

        Assert.NotEqual(a, b);
        Assert.StartsWith("verificationdocument/2026/11/", a, StringComparison.Ordinal);
    }

    [Fact]
    public void File_storage_offers_no_delete_operation()
    {
        var methods = typeof(IFileStorage).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain(methods, n => n.Contains("Delete", StringComparison.OrdinalIgnoreCase) || n.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
