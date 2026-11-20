using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Gifts.Common;

namespace Qonaqet.Application.Features.Gifts.Jobs;

/// <summary>
/// Gözləyən hədiyyə refundları (Worker, 15 dəqiqədə bir): ləğv və ya müddət bitməsində provayder əlçatmaz olubsa, refund burada təkrarlanır.
/// <see cref="GiftRefunder"/> idempotentdir — eyni hədiyyə üçün ikinci refund yaranmır.
/// </summary>
public sealed class RetryGiftRefundsJob(IGiftRepository gifts, GiftRefunder refunder) : IScheduledJob
{
    public const string Name = "gift-refunds";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public const int BatchSize = 100;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var due = await gifts.ListRefundDueAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        var refunded = 0;
        foreach (var gift in due)
        {
            if (await refunder.RefundAsync(gift, cancellationToken).ConfigureAwait(false))
            {
                refunded++;
            }
        }

        return refunded;
    }
}
