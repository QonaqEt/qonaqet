using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Files;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Corporate.Queries.CorporateReports;

/// <summary>Endirilən fayl (CSV/PDF).</summary>
public sealed record ReportFileDto(byte[] Content, string ContentType, string FileName);

public sealed record CorporateCampaignDto(Guid Id, string Title, CampaignStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, long GoalQepik, long RaisedQepik);

public sealed record CorporateReportDto(Guid Id, CorporateReportSubject Subject, Guid SubjectId, CorporateReportStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

/// <summary><c>GET corporate/orders/{id}/report</c>: çatdırılan/açılan/təhvil verilən saylar, istifadə olunmamış dəyər, alıcı üzrə yalnız bəli/xeyr.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetCorporateOrderReportQuery(CorporateGiftOrderId OrderId) : IQuery<CorporateOrderReportDto>;

/// <summary><c>GET corporate/orders/{id}/report.csv</c>.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record ExportCorporateOrderReportCsvQuery(CorporateGiftOrderId OrderId) : IQuery<ReportFileDto>;

/// <summary><c>GET corporate/campaigns</c>: şirkətin CSR kampaniyaları.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record ListCorporateCampaignsQuery() : IQuery<IReadOnlyList<CorporateCampaignDto>>;

/// <summary><c>GET corporate/campaigns/{id}/report</c>: yalnız aqreqatlar.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetCampaignReportQuery(CampaignId CampaignId) : IQuery<CampaignReportDto>;

/// <summary><c>GET corporate/campaigns/{id}/report.csv</c>.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record ExportCampaignReportCsvQuery(CampaignId CampaignId) : IQuery<ReportFileDto>;

/// <summary><c>GET corporate/reports/{id}</c>: PDF hesabatın vəziyyəti.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetCorporateReportQuery(CorporateReportId ReportId) : IQuery<CorporateReportDto>;

/// <summary><c>GET corporate/reports/{id}/file</c>: hazır PDF.</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record DownloadCorporateReportQuery(CorporateReportId ReportId) : IQuery<ReportFileDto>;

/// <summary>Şirkətin öz kampaniyası; başqasınınkı tapılmır.</summary>
public sealed class CorporateCampaigns(CorporateAccess access, ICampaignRepository campaigns)
{
    public async Task<Result<(Domain.Corporate.Corporate Corporate, Campaign Campaign)>> RequireAsync(CampaignId campaignId, CancellationToken cancellationToken)
    {
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        var campaign = await campaigns.GetAsync(campaignId, cancellationToken).ConfigureAwait(false);
        if (campaign is null || campaign.CorporateId != corporate.Value.Id.Value)
        {
            return CorporateErrors.ReportNotFound;
        }

        return (corporate.Value, campaign);
    }
}

public sealed class GetCorporateOrderReportHandler(CorporateAccess access, CorporateReportBuilder builder)
    : IQueryHandler<GetCorporateOrderReportQuery, CorporateOrderReportDto>
{
    public async Task<Result<CorporateOrderReportDto>> Handle(GetCorporateOrderReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await access.RequireOrderAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        return found.IsFailure ? found.Error : await builder.OrderAsync(found.Value.Order, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ExportCorporateOrderReportCsvHandler(CorporateAccess access, CorporateReportBuilder builder, ICsvService csv)
    : IQueryHandler<ExportCorporateOrderReportCsvQuery, ReportFileDto>
{
    public async Task<Result<ReportFileDto>> Handle(ExportCorporateOrderReportCsvQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await access.RequireOrderAsync(request.OrderId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var report = await builder.OrderAsync(found.Value.Order, cancellationToken).ConfigureAwait(false);
        var bytes = await CorporateReportBuilder.CsvAsync(csv, CorporateReportBuilder.Table(report, found.Value.Corporate.Name), cancellationToken)
            .ConfigureAwait(false);
        return new ReportFileDto(bytes, FilePolicy.Csv, $"qonaqet-sifaris-{report.OrderId:N}.csv");
    }
}

public sealed class ListCorporateCampaignsHandler(CorporateAccess access, ICampaignRepository campaigns)
    : IQueryHandler<ListCorporateCampaignsQuery, IReadOnlyList<CorporateCampaignDto>>
{
    public async Task<Result<IReadOnlyList<CorporateCampaignDto>>> Handle(ListCorporateCampaignsQuery request, CancellationToken cancellationToken)
    {
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        var list = await campaigns.ListForCorporateAsync(corporate.Value.Id.Value, 200, cancellationToken).ConfigureAwait(false);
        return list.Select(c => new CorporateCampaignDto(c.Id.Value, c.Title, c.Status, c.StartsAt, c.EndsAt, c.GoalQepik, c.RaisedQepik)).ToList();
    }
}

public sealed class GetCampaignReportHandler(CorporateCampaigns campaigns, CorporateReportBuilder builder) : IQueryHandler<GetCampaignReportQuery, CampaignReportDto>
{
    public async Task<Result<CampaignReportDto>> Handle(GetCampaignReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await campaigns.RequireAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        return found.IsFailure ? found.Error : await builder.CampaignAsync(found.Value.Campaign, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ExportCampaignReportCsvHandler(CorporateCampaigns campaigns, CorporateReportBuilder builder, ICsvService csv)
    : IQueryHandler<ExportCampaignReportCsvQuery, ReportFileDto>
{
    public async Task<Result<ReportFileDto>> Handle(ExportCampaignReportCsvQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await campaigns.RequireAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var report = await builder.CampaignAsync(found.Value.Campaign, cancellationToken).ConfigureAwait(false);
        var bytes = await CorporateReportBuilder.CsvAsync(csv, CorporateReportBuilder.Table(report, found.Value.Corporate.Name), cancellationToken)
            .ConfigureAwait(false);
        return new ReportFileDto(bytes, FilePolicy.Csv, $"qonaqet-csr-{report.CampaignId:N}.csv");
    }
}

public sealed class GetCorporateReportHandler(CorporateAccess access, ICorporateReportRepository reports)
    : IQueryHandler<GetCorporateReportQuery, CorporateReportDto>
{
    public async Task<Result<CorporateReportDto>> Handle(GetCorporateReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var report = await CorporateReportAccess.RequireAsync(access, reports, request.ReportId, cancellationToken).ConfigureAwait(false);
        return report.IsFailure ? report.Error : report.Value.ToDto();
    }
}

public sealed class DownloadCorporateReportHandler(
    CorporateAccess access, ICorporateReportRepository reports, IStoredFileRepository files, IFileStorage storage)
    : IQueryHandler<DownloadCorporateReportQuery, ReportFileDto>
{
    public async Task<Result<ReportFileDto>> Handle(DownloadCorporateReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var report = await CorporateReportAccess.RequireAsync(access, reports, request.ReportId, cancellationToken).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return report.Error;
        }

        if (report.Value is not { Status: CorporateReportStatus.Ready, FileId: { } fileId })
        {
            return CorporateErrors.ReportNotReady;
        }

        var file = await files.GetAsync(FileId.From(fileId), cancellationToken).ConfigureAwait(false);
        var content = file is null ? null : await storage.OpenReadAsync(file.ObjectKey, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return CorporateErrors.ReportNotReady;
        }

        await using var stream = content.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new ReportFileDto(buffer.ToArray(), FilePolicy.Pdf, $"qonaqet-hesabat-{report.Value.Id.Value:N}.pdf");
    }
}

public static class CorporateReportAccess
{
    public static async Task<Result<CorporateReport>> RequireAsync(
        CorporateAccess access, ICorporateReportRepository reports, CorporateReportId reportId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(reports);
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (corporate.IsFailure)
        {
            return corporate.Error;
        }

        var report = await reports.GetAsync(reportId, cancellationToken).ConfigureAwait(false);
        return report is null || report.CorporateId != corporate.Value.Id ? CorporateErrors.ReportNotFound : report;
    }

    public static CorporateReportDto ToDto(this CorporateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new CorporateReportDto(report.Id.Value, report.Subject, report.SubjectId, report.Status, report.CreatedAt, report.CompletedAt);
    }
}
