using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Integration;

/// <summary>
/// Issuer partnyorun hədiyyəsi balansdan ödənildi (Postgres: <c>PartnerIssue</c> ledger yazısı ilə eyni tranzaksiyada). Consumer hədiyyəni
/// <c>Scheduled</c> edir — API-nin sürətli yolu yarıda qalsa da hədiyyə yaranır (ödəniş təsdiqi ilə eyni nümunə).
/// </summary>
public sealed record PartnerGiftFunded(Guid GiftId, Guid ClientId, long AmountQepik, DateTimeOffset At) : DomainEvent(At);
