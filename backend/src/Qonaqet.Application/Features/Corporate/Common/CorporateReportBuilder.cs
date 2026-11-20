using System.Globalization;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Corporate.Common;

/// <summary>Alıcı üzrə sətir: yalnız email və "təhvil verildi: bəli/xeyr" — nə aldığı, harada, nə vaxt göstərilmir.</summary>
public sealed record CorporateRecipientRowDto(string Email, bool Redeemed);

/// <summary>Korporativ sifarişin hesabatı: saylar və istifadə olunmamış dəyər; alıcı sətirləri yalnız email + bəli/xeyr.</summary>
public sealed record CorporateOrderReportDto(
    Guid OrderId,
    CorporateOrderStatus Status,
    int RecipientCount,
    int GiftsCreated,
    int Delivered,
    int Opened,
    int Redeemed,
    long TotalQepik,
    long RedeemedValueQepik,
    long UnusedValueQepik,
    IReadOnlyList<CorporateRecipientRowDto> Recipients);

/// <summary>CSR kampaniyasının hesabatı: yalnız aqreqatlar (heç bir şəxs, alıcı, ianəçi və ya kateqoriya üzvü yoxdur).</summary>
public sealed record CampaignReportDto(
    Guid CampaignId,
    string Title,
    CampaignStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    long GoalQepik,
    long RaisedQepik,
    int Donations,
    long TreatsFunded,
    long TreatsValueQepik);

/// <summary>Kampaniyanın CSR aqreqatları (Infrastructure: xam sorğular).</summary>
public sealed record CampaignCsrStats(int Donations, long TreatsFunded, long TreatsValueQepik);

public interface ICsrStatsReader
{
    Task<CampaignCsrStats> CampaignStatsAsync(Guid campaignId, CancellationToken cancellationToken);
}

/// <summary>Hesabatın cədvəl forması (PDF və CSV eyni məlumatdan).</summary>
public sealed record ReportTable(string Title, string Subtitle, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Korporativ və CSR hesabatlarının qurulması.</summary>
public sealed class CorporateReportBuilder(IGiftRepository gifts, ICsrStatsReader csr)
{
    public async Task<CorporateOrderReportDto> OrderAsync(CorporateGiftOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var list = await gifts.ListForCorporateOrderAsync(order.Id.Value, cancellationToken).ConfigureAwait(false);
        var rows = list.OrderBy(g => g.CorporateRecipientIndex)
            .Select(g => new CorporateRecipientRowDto(g.RecipientEmail ?? string.Empty, g.RedeemedQepik > 0))
            .ToList();
        return new CorporateOrderReportDto(
            order.Id.Value,
            order.Status,
            order.RecipientCount,
            list.Count,
            list.Count(g => g.DeliveredAt is not null),
            list.Count(g => g.OpenedAt is not null),
            list.Count(g => g.RedeemedQepik > 0),
            order.TotalQepik,
            list.Sum(g => g.RedeemedQepik),
            list.Where(g => g.Status is not GiftStatus.Cancelled).Sum(g => g.RemainingQepik),
            rows);
    }

    public async Task<CampaignReportDto> CampaignAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var stats = await csr.CampaignStatsAsync(campaign.Id.Value, cancellationToken).ConfigureAwait(false);
        return new CampaignReportDto(
            campaign.Id.Value, campaign.Title, campaign.Status, campaign.StartsAt, campaign.EndsAt, campaign.GoalQepik, campaign.RaisedQepik,
            stats.Donations, stats.TreatsFunded, stats.TreatsValueQepik);
    }

    public static ReportTable Table(CorporateOrderReportDto report, string corporateName)
    {
        ArgumentNullException.ThrowIfNull(report);
        var subtitle = string.Create(CultureInfo.InvariantCulture,
            $"{corporateName} · alıcılar: {report.RecipientCount} · çatdırılıb: {report.Delivered} · açılıb: {report.Opened} · təhvil verilib: {report.Redeemed} · "
            + $"istifadə olunmamış dəyər: {Money.FromQepik(report.UnusedValueQepik)}");
        return new ReportTable(
            "Korporativ hədiyyə hesabatı", subtitle, ["Email", "Təhvil verildi"],
            [.. report.Recipients.Select(r => (IReadOnlyList<string>)[r.Email, r.Redeemed ? "bəli" : "xeyr"])]);
    }

    public static ReportTable Table(CampaignReportDto report, string corporateName)
    {
        ArgumentNullException.ThrowIfNull(report);
        var period = $"{GiftDate(report.StartsAt)} – {GiftDate(report.EndsAt)}";
        return new ReportTable(
            $"CSR hesabatı: {report.Title}", $"{corporateName} · {period}", ["Göstərici", "Dəyər"],
            [
                ["Hədəf", Money.FromQepik(report.GoalQepik).ToString()],
                ["Toplanıb", Money.FromQepik(report.RaisedQepik).ToString()],
                ["İanələr", report.Donations.ToString(CultureInfo.InvariantCulture)],
                ["Qonaq olunanlar", report.TreatsFunded.ToString(CultureInfo.InvariantCulture)],
                ["Qonaqlıqların dəyəri", Money.FromQepik(report.TreatsValueQepik).ToString()],
                ["Status", report.Status.ToString()],
            ]);
    }

    /// <summary>CSV (öz yazıcımızla): eyni cədvəl.</summary>
    public static async Task<byte[]> CsvAsync(ICsvService csv, ReportTable table, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(table);
        using var stream = new MemoryStream();
        await csv.WriteAsync(stream, table.Headers, table.Rows, cancellationToken).ConfigureAwait(false);
        return stream.ToArray();
    }

    private static string GiftDate(DateTimeOffset instant) =>
        BakuTime.ToBakuDate(instant).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
