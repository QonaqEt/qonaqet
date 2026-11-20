using System.Globalization;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Application.Features.Corporate.Queries.CorporateReports;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Files;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Corporate.Commands.CorporateReports;

/// <summary>
/// <c>POST corporate/orders/{id}/report.pdf</c> və <c>POST corporate/campaigns/{id}/report.pdf</c>: PDF hesabat sorğusu. PDF Worker-də (Playwright)
/// yaradılır; cavab sorğunun ID-sidir, klient <c>GET corporate/reports/{id}</c> ilə hazır olmasını gözləyir və faylı endirir.
/// </summary>
[RequiresRole(RoleNames.CorporateUser)]
[RequiresActiveAccount]
public sealed record RequestCorporateReportCommand(CorporateReportSubject Subject, Guid SubjectId) : ICommand<CorporateReportDto>;

public sealed class RequestCorporateReportHandler(
    CorporateAccess access, CorporateCampaigns campaigns, ICorporateReportRepository reports, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RequestCorporateReportCommand, CorporateReportDto>
{
    public async Task<Result<CorporateReportDto>> Handle(RequestCorporateReportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Domain.Corporate.Corporate corporate;
        if (request.Subject == CorporateReportSubject.GiftOrder)
        {
            var found = await access.RequireOrderAsync(CorporateGiftOrderId.From(request.SubjectId), cancellationToken).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            corporate = found.Value.Corporate;
        }
        else
        {
            var found = await campaigns.RequireAsync(CampaignId.From(request.SubjectId), cancellationToken).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            corporate = found.Value.Corporate;
        }

        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var report = CorporateReport.Request(corporate.Id, request.Subject, request.SubjectId, userId, clock.UtcNow);
        await reports.AddAsync(report, cancellationToken).ConfigureAwait(false);
        return report.ToDto();
    }
}

/// <summary>
/// PDF hesabatın yaradılması (Worker consumer-i çağırır): cədvəl qurulur, ortaq <c>report</c> şablonu ilə HTML → PDF, fayl <c>files</c>-a
/// (<c>Report</c>) yazılır və sorğu <c>Ready</c> olur. Xəta olarsa sorğu <c>Failed</c> olur (klient yenisini istəyə bilər). İdempotentdir:
/// yalnız <c>Pending</c> sorğu emal olunur.
/// </summary>
public sealed class CorporateReportRenderer(
    ICorporateReportRepository reports,
    ICorporateRepository corporates,
    ICorporateGiftOrderRepository orders,
    ICampaignRepository campaigns,
    CorporateReportBuilder builder,
    IPdfRenderer pdf,
    FileUploader uploader,
    IClock clock)
{
    public const string Template = "report";

    public async Task<bool> RenderAsync(CorporateReportId reportId, CancellationToken cancellationToken)
    {
        var report = await reports.GetAsync(reportId, cancellationToken).ConfigureAwait(false);
        if (report is not { Status: CorporateReportStatus.Pending })
        {
            return false;
        }

        try
        {
            var table = await TableAsync(report, cancellationToken).ConfigureAwait(false);
            var model = new Dictionary<string, string?>
            {
                ["title"] = table.Title,
                ["subtitle"] = table.Subtitle,
                ["generatedAt"] = BakuTime.ToBakuTime(clock.UtcNow).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            };
            var bytes = await pdf.RenderTemplateAsync(Template, model, new PdfTable(table.Headers, table.Rows), cancellationToken).ConfigureAwait(false);
            var file = await uploader.UploadAsync(
                new FileUpload(FilePurpose.Report, FilePolicy.Pdf, bytes), FileOwnerType.Corporate, report.CorporateId.Value, report.CreatedBy, cancellationToken)
                .ConfigureAwait(false);
            if (file.IsFailure)
            {
                throw new InvalidOperationException($"Hesabat faylı saxlanılmadı: {file.Error.Code}");
            }

            report.Complete(file.Value.Id.Value, clock.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Fail(clock.UtcNow);
        }

        await reports.UpdateAsync(report, cancellationToken).ConfigureAwait(false);
        return report.Status == CorporateReportStatus.Ready;
    }

    private async Task<ReportTable> TableAsync(CorporateReport report, CancellationToken cancellationToken)
    {
        var corporate = await corporates.GetAsync(report.CorporateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Şirkət tapılmadı.");
        if (report.Subject == CorporateReportSubject.GiftOrder)
        {
            var order = await orders.GetAsync(CorporateGiftOrderId.From(report.SubjectId), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Sifariş tapılmadı.");
            return CorporateReportBuilder.Table(await builder.OrderAsync(order, cancellationToken).ConfigureAwait(false), corporate.Name);
        }

        var campaign = await campaigns.GetAsync(CampaignId.From(report.SubjectId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Kampaniya tapılmadı.");
        return CorporateReportBuilder.Table(await builder.CampaignAsync(campaign, cancellationToken).ConfigureAwait(false), corporate.Name);
    }
}
