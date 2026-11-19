using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçi bloklandı. Consumer-lər: aktiv claim-lər ləğv olunur və ehtiyat buraxılır (E06-T08), lövhələrdən gizlənir (E11).
/// Səbəb və şəxsi məlumat daşımır.
/// </summary>
public sealed record UserBlocked(UserId UserId, DateTimeOffset At) : DomainEvent(At);

public sealed record UserUnblocked(UserId UserId, DateTimeOffset At) : DomainEvent(At);
