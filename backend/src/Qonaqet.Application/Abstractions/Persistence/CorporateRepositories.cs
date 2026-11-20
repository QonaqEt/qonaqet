using Qonaqet.Domain.Corporate;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>corporates</c>.</summary>
public interface ICorporateRepository : IRepository<Corporate, CorporateId>
{
    /// <summary>VÖEN silinməmiş başqa şirkətdə işlənirmi.</summary>
    Task<bool> VoenExistsAsync(string voen, CorporateId? except, CancellationToken cancellationToken);

    /// <summary>Şirkətlər ada görə.</summary>
    Task<IReadOnlyList<Corporate>> ListAsync(int limit, CancellationToken cancellationToken);
}

/// <summary><c>corporateGiftOrders</c>.</summary>
public interface ICorporateGiftOrderRepository : IRepository<CorporateGiftOrder, CorporateGiftOrderId>
{
    /// <summary>Sifarişi yalnız hələ <paramref name="expected"/> statusunda və <paramref name="expectedSent"/> irəliləyişindədirsə yazır (optimistik).</summary>
    Task<bool> TryReplaceAsync(CorporateGiftOrder order, CorporateOrderStatus expected, int expectedSent, CancellationToken cancellationToken);

    /// <summary>Şirkətin sifarişləri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<CorporateGiftOrder>> ListForCorporateAsync(CorporateId corporateId, int limit, CancellationToken cancellationToken);

    /// <summary>Hədiyyələri yaradılmalı (<c>Paid</c>) və ya yarımçıq qalmış (<c>Sending</c>) sifarişlər, ən köhnələr əvvəl.</summary>
    Task<IReadOnlyList<CorporateGiftOrder>> ListToFanOutAsync(int limit, CancellationToken cancellationToken);
}

/// <summary><c>corporateReports</c>: PDF hesabat sorğuları.</summary>
public interface ICorporateReportRepository : IRepository<CorporateReport, CorporateReportId>;
