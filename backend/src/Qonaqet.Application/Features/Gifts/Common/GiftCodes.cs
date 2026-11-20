using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>Hədiyyənin təhvil kodları: QR payload, klientin çəkdiyi modul matrisi və qısa kod (<c>XXXX-XXXX</c>).</summary>
public sealed record GiftCodesDto(string QrPayload, QrMatrix Qr, string ShortCode);

/// <summary>
/// Hədiyyə kodları ID-dən server sirri ilə törədilir (saxlanılmır): QR <c>base64url("g." + id + "." + HMAC)</c> tam təhvilə qədər təkrar
/// işlənir, qısa kod hədiyyə üçün sabitdir.
/// </summary>
public static class GiftCodes
{
    public static string ShortCodeHash(IRedemptionCodes codes, GiftId giftId)
    {
        ArgumentNullException.ThrowIfNull(codes);
        return codes.ShortCodeHash(codes.ShortCode(RedemptionCodeKind.Gift, giftId.Value))
            ?? throw new InvalidOperationException("Qısa kod yaradıla bilmədi.");
    }

    public static GiftCodesDto Present(IRedemptionCodes codes, IQrCodeEncoder qr, GiftId giftId)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(qr);
        var payload = codes.QrPayload(RedemptionCodeKind.Gift, giftId.Value);
        var shortCode = codes.ShortCode(RedemptionCodeKind.Gift, giftId.Value);
        return new GiftCodesDto(payload, qr.Encode(payload), $"{shortCode[..4]}-{shortCode[4..]}");
    }
}
