using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Payments.Jobs;

/// <summary>
/// Ödənişlərin yenidən yoxlanması (Worker, 5 dəqiqədə bir): callback itsə belə, 2 dəqiqədən köhnə <c>Pending</c> ödənişlər Get Order ilə
/// təsdiqlənir; 30 dəqiqədən köhnə və hələ ödənilməmiş olanlar <c>Expired</c> olur. Hər keçid status keçidi + versiya nişanı ilə qorunur, ona görə
/// iki instans və ya eyni anda gələn callback ödənişi iki dəfə təsdiqləmir.
/// </summary>
public sealed class RecheckPaymentsJob(IPaymentRepository payments, PaymentConfirmation confirmation, IClock clock) : IScheduledJob
{
    public const string Name = "payment-recheck";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Callback-ə vaxt verilir: bundan təzə ödənişlərə toxunulmur.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);

    public const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var pending = await payments.ListPendingAsync(clock.UtcNow - MinAge, BatchSize, cancellationToken).ConfigureAwait(false);
        var ids = pending.Select(p => p.Id).ToArray();
        payments.Reset();

        var changed = 0;
        foreach (var id in ids)
        {
            var outcome = await confirmation.CheckAsync(id, expireIfStale: true, cancellationToken).ConfigureAwait(false);
            if (outcome is PaymentCheckOutcome.Paid or PaymentCheckOutcome.Failed or PaymentCheckOutcome.Expired)
            {
                changed++;
            }
        }

        return changed;
    }
}
