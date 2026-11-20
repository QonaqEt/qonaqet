using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Queries.MyGifts;

/// <summary><c>GET gifts/sent</c>: göndərdiyim hədiyyələr, ən yenilər əvvəl.</summary>
public sealed record GetSentGiftsQuery() : IQuery<IReadOnlyList<SentGiftDto>>;

/// <summary><c>GET gifts/received</c>: hesabıma bağlı (çatdırılmış) hədiyyələr — kodları ilə.</summary>
public sealed record GetReceivedGiftsQuery() : IQuery<IReadOnlyList<ReceivedGiftDto>>;

/// <summary><c>GET gifts/{id}</c>: göndərən göndərən görünüşünü, alıcı alıcı görünüşünü alır; başqaları üçün hədiyyə yoxdur.</summary>
public sealed record GetGiftQuery(GiftId GiftId) : IQuery<GiftDetailsDto>;

/// <param name="Role"><c>Sender</c> və ya <c>Recipient</c>.</param>
public sealed record GiftDetailsDto(string Role, SentGiftDto? Sent, ReceivedGiftDto? Received);

internal static class MyGiftsLimits
{
    public const int ListLimit = 100;
}

public sealed class GetSentGiftsHandler(IGiftRepository gifts, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetSentGiftsQuery, IReadOnlyList<SentGiftDto>>
{
    public async Task<Result<IReadOnlyList<SentGiftDto>>> Handle(GetSentGiftsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var sent = await gifts.ListSentAsync(userId, MyGiftsLimits.ListLimit, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        return sent.Select(g => GiftViews.Sent(g, now)).ToList();
    }
}

public sealed class GetReceivedGiftsHandler(IGiftRepository gifts, GiftPresenter presenter, ICurrentUser currentUser)
    : IQueryHandler<GetReceivedGiftsQuery, IReadOnlyList<ReceivedGiftDto>>
{
    public async Task<Result<IReadOnlyList<ReceivedGiftDto>>> Handle(GetReceivedGiftsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var received = await gifts.ListReceivedAsync(userId, MyGiftsLimits.ListLimit, cancellationToken).ConfigureAwait(false);
        var items = new List<ReceivedGiftDto>(received.Count);
        foreach (var gift in received)
        {
            items.Add(new ReceivedGiftDto(gift.Id.Value, await presenter.PresentAsync(gift, cancellationToken).ConfigureAwait(false)));
        }

        return items;
    }
}

public sealed class GetGiftHandler(IGiftRepository gifts, GiftPresenter presenter, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetGiftQuery, GiftDetailsDto>
{
    public async Task<Result<GiftDetailsDto>> Handle(GetGiftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        if (gift is null)
        {
            return GiftErrors.NotFound;
        }

        if (gift.SenderUserId == userId)
        {
            return new GiftDetailsDto("Sender", GiftViews.Sent(gift, clock.UtcNow), null);
        }

        if (gift.RecipientUserId == userId && gift.DeliveredAt is not null)
        {
            return new GiftDetailsDto("Recipient", null, new ReceivedGiftDto(gift.Id.Value, await presenter.PresentAsync(gift, cancellationToken).ConfigureAwait(false)));
        }

        return GiftErrors.NotFound;
    }
}
