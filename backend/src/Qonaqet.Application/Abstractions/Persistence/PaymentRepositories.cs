using Qonaqet.Domain.Payments;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary>
/// <c>payments</c> və <c>refunds</c> (Postgres). Dəyişikliklər <see cref="SaveChangesAsync"/> ilə yazılır; aqreqat hadisələri eyni
/// <c>SaveChanges</c>-də Postgres outbox-una düşür (interceptor). Açıq Postgres tranzaksiyası varsa onun hissəsidir.
/// <see cref="SaveChangesAsync"/> paralel dəyişiklikdə (versiya fərqi) <see cref="ConcurrencyConflictException"/> atır.
/// </summary>
public interface IPaymentRepository
{
    void Add(Payment payment);

    void AddRefund(Refund refund);

    Task<Payment?> GetAsync(PaymentId id, CancellationToken cancellationToken);

    Task<Payment?> FindByProviderOrderIdAsync(string providerOrderId, CancellationToken cancellationToken);

    /// <summary><paramref name="createdBefore"/>-dən əvvəl yaradılmış <c>Pending</c> ödənişlər, köhnədən yeniyə.</summary>
    Task<IReadOnlyList<Payment>> ListPendingAsync(DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Refund>> ListRefundsAsync(PaymentId paymentId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>İzlənən dəyişiklikləri atır (paralellik xətasından sonra yenidən oxumaq üçün).</summary>
    void Reset();
}

/// <summary>Optimistik paralellik: qeyd başqa sorğu tərəfindən dəyişdirilib.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
