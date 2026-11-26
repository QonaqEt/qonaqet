// QONAQET webhook imzasının yoxlanması — C# (.NET 8+). Özünü yoxlama: `dotnet run webhook-verify.cs` (.NET 10).
using System.Security.Cryptography;
using System.Text;

// Test vektoru (docs/integration-guide.md → "Test vektoru").
var ok = QonaqetWebhook.Verify("whsec_test", "1700000000", """{"a":1}""",
    "sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789", DateTimeOffset.FromUnixTimeSeconds(1700000060));
var stale = QonaqetWebhook.Verify("whsec_test", "1700000000", """{"a":1}""",
    "sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789", DateTimeOffset.FromUnixTimeSeconds(1700000301));
Console.WriteLine(ok && !stale ? "ok" : "FAIL");
return ok && !stale ? 0 : 1;

static class QonaqetWebhook
{
    /// <param name="body">Sorğunun xam body-si (bayt-bayt, JSON-u yenidən serializasiya etmədən).</param>
    public static bool Verify(string secret, string timestamp, string body, string signatureHeader, DateTimeOffset now)
    {
        // 5 dəqiqədən köhnə (və ya gələcəkdən) sorğunu rədd edin — replay qoruması.
        if (!long.TryParse(timestamp, out var ts) || Math.Abs(now.ToUnixTimeSeconds() - ts) > 300)
        {
            return false;
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        var expected = Encoding.ASCII.GetBytes("sha256=" + Convert.ToHexStringLower(mac));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signatureHeader ?? string.Empty));
    }
}
