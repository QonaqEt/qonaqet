using System.Text.Json;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Payments.Commands.HandlePaymentCallback;

/// <summary>
/// <c>POST payments/payriff/callback</c> (anonim, rate limit). Callback <b>imzasızdır</b>: body-dən yalnız sifariş ID-si götürülür, status isə həmişə
/// Get Order ilə təsdiqlənir. Saxta və ya naməlum callback heç nəyi dəyişmir və eyni cavabı alır (məlumat sızmasın deyə). Body loglanmır.
/// Fake ödəniş səhifəsi də ödənişdən sonra bu əmri göndərir (callback simulyasiyası).
/// </summary>
[PublicRequest]
public sealed record HandlePaymentCallbackCommand(JsonElement? Body, string? OrderId = null) : ICommand, IPostgresCommand, INonTransactionalCommand;

public sealed class HandlePaymentCallbackHandler(IPaymentRepository payments, PaymentConfirmation confirmation) : ICommandHandler<HandlePaymentCallbackCommand>
{
    public const int MaxOrderIdLength = 128;

    public async Task<Result> Handle(HandlePaymentCallbackCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var orderId = request.OrderId ?? OrderIdFrom(request.Body);
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > MaxOrderIdLength)
        {
            return Result.Success();
        }

        var payment = await payments.FindByProviderOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (payment is not null)
        {
            await confirmation.CheckAsync(payment.Id, expireIfStale: false, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>Sifariş ID-si: <c>orderId</c> və ya <c>payload.orderId</c> (Payriff cavab zərfi).</summary>
    public static string? OrderIdFrom(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        if (Find(root, "orderId") is { } direct)
        {
            return direct;
        }

        return root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object ? Find(payload, "orderId") : null;
    }

    private static string? Find(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
