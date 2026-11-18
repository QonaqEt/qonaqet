using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// <see cref="IIdempotentCommand"/> üçün: eyni aktor + açar + body → saxlanmış uğurlu cavab təkrar verilir
/// (handler yenidən işləmir); eyni açar + fərqli body → <c>409 idempotency_key_reused</c>.
/// Uğursuz cavablar saxlanmır ki, düzəldilmiş sorğu eyni açarla təkrar göndərilə bilsin.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IRequestContext requestContext,
    ICurrentUser currentUser,
    IIdempotencyStore store,
    IClock clock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public const int MaxKeyLength = 200;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StronglyTypedIdJsonConverterFactory() },
    };

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not IIdempotentCommand)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var key = requestContext.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return ResultFactory<TResponse>.Failure(CommonErrors.IdempotencyKeyRequired);
        }

        var scope = $"{typeof(TRequest).FullName}:{currentUser.ActorId?.ToString("N") ?? "anonymous"}";
        var requestHash = Hash(request);

        var existing = await store.FindAsync(scope, key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Replay(existing, requestHash);
        }

        var response = await next(cancellationToken).ConfigureAwait(false);
        if (response is not Result { IsSuccess: true })
        {
            return response;
        }

        var record = new IdempotencyRecord(scope, key, requestHash, ResultFactory<TResponse>.SerializeValue(response, JsonOptions), clock.UtcNow);
        var stored = await store.AddAsync(record, cancellationToken).ConfigureAwait(false);

        // Paralel sorğu bizdən əvvəl yazıbsa, onun cavabı qalib gəlir.
        return ReferenceEquals(stored, record) || stored == record ? response : Replay(stored, requestHash);
    }

    private static TResponse Replay(IdempotencyRecord record, string requestHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(record.RequestHash), Encoding.ASCII.GetBytes(requestHash))
            ? ResultFactory<TResponse>.DeserializeSuccess(record.ResponseJson, JsonOptions)
            : ResultFactory<TResponse>.Failure(CommonErrors.IdempotencyKeyReused);

    private static string Hash(TRequest request)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), JsonOptions);
        return Convert.ToHexString(SHA256.HashData(json));
    }
}
