using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Corporate;

public readonly record struct CorporateReportId(Guid Value) : IStronglyTypedId<CorporateReportId>
{
    public static CorporateReportId From(Guid value) => new(value);
}

/// <summary>Hesabatın mövzusu: korporativ hədiyyə sifarişi və ya CSR kampaniyası.</summary>
public enum CorporateReportSubject
{
    GiftOrder,
    Campaign,
}

public enum CorporateReportStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>
/// PDF hesabatın sorğusu (<c>corporateReports</c>): PDF Worker-də (headless Chromium) yaradılır, ona görə sorğu asinxrondur — Api qeydi yaradır,
/// Worker <see cref="CorporateReportRequested"/>-i emal edib faylı saxlayır və qeydi <c>Ready</c> edir. Hesabatlarda şəxsi məlumat yoxdur
/// (sifarişdə yalnız alıcının email-i və "təhvil verildi: bəli/xeyr"; CSR-da yalnız aqreqatlar).
/// </summary>
public sealed class CorporateReport : AggregateRoot<CorporateReportId>
{
    private CorporateReport(CorporateReportId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private CorporateReport()
    {
    }

    public CorporateId CorporateId { get; private set; }

    public CorporateReportSubject Subject { get; private set; }

    public Guid SubjectId { get; private set; }

    public CorporateReportStatus Status { get; private set; }

    public Guid? FileId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static CorporateReport Request(CorporateId corporateId, CorporateReportSubject subject, Guid subjectId, Guid requestedBy, DateTimeOffset at)
    {
        var report = new CorporateReport(StronglyTypedId.New<CorporateReportId>(), Guard.Utc(at), requestedBy)
        {
            CorporateId = corporateId,
            Subject = subject,
            SubjectId = Guard.NotEmpty(subjectId),
            Status = CorporateReportStatus.Pending,
        };
        report.Raise(new CorporateReportRequested(report.Id, at));
        return report;
    }

    public bool Complete(Guid fileId, DateTimeOffset at)
    {
        if (Status != CorporateReportStatus.Pending)
        {
            return false;
        }

        Status = CorporateReportStatus.Ready;
        FileId = Guard.NotEmpty(fileId);
        CompletedAt = Guard.Utc(at);
        MarkUpdated(at, null);
        return true;
    }

    public bool Fail(DateTimeOffset at)
    {
        if (Status != CorporateReportStatus.Pending)
        {
            return false;
        }

        Status = CorporateReportStatus.Failed;
        CompletedAt = Guard.Utc(at);
        MarkUpdated(at, null);
        return true;
    }
}

/// <summary>PDF hesabatın yaradılması lazımdır (Worker consumer-i).</summary>
public sealed record CorporateReportRequested(CorporateReportId ReportId, DateTimeOffset At) : DomainEvent(At);
