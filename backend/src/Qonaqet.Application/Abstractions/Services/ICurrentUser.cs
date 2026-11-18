namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Sorğunu edən aktor (istifadəçi və ya inteqrasiya klienti). Host-lar JWT-dən doldurur (E02).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>İstifadəçi və ya inteqrasiya klientinin ID-si.</summary>
    Guid? ActorId { get; }

    IReadOnlySet<string> Roles { get; }
}

/// <summary>Sorğu səviyyəli kontekst (HTTP başlıqlarından).</summary>
public interface IRequestContext
{
    /// <summary><c>Idempotency-Key</c> başlığı; yoxdursa <c>null</c>.</summary>
    string? IdempotencyKey { get; }
}
