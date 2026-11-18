using System.Reflection;
using System.Text.Json;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// Behavior-ların generik <typeparamref name="TResponse"/> üçün nəticə yaratması. Bütün sorğular
/// <see cref="Result"/> və ya <see cref="Result{TValue}"/> qaytarır; delegatlar tip başına bir dəfə qurulur.
/// </summary>
internal static class ResultFactory<TResponse>
{
    private static readonly Type? ValueType = typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>)
        ? typeof(TResponse).GetGenericArguments()[0]
        : null;

    private static readonly Func<Error, TResponse> FailureFactory = BuildFailure();

    private static readonly Func<object, TResponse>? SuccessFactory = BuildSuccess();

    public static bool IsResult => typeof(TResponse) == typeof(Result) || ValueType is not null;

    public static TResponse Failure(Error error) => FailureFactory(error);

    /// <summary>Uğurlu cavabın dəyərini JSON-a yazır (dəyərsiz <see cref="Result"/> üçün <c>null</c>).</summary>
    public static string SerializeValue(TResponse response, JsonSerializerOptions options)
    {
        if (ValueType is null)
        {
            return "null";
        }

        var value = typeof(TResponse).GetProperty(nameof(Result<int>.Value))!.GetValue(response);
        return JsonSerializer.Serialize(value, ValueType, options);
    }

    public static TResponse DeserializeSuccess(string json, JsonSerializerOptions options)
    {
        if (ValueType is null)
        {
            return (TResponse)(object)Result.Success();
        }

        var value = JsonSerializer.Deserialize(json, ValueType, options)
            ?? throw new JsonException("Saxlanmış idempotent cavab boşdur.");
        return SuccessFactory!(value);
    }

    private static Func<Error, TResponse> BuildFailure()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return error => (TResponse)(object)Result.Failure(error);
        }

        if (ValueType is null)
        {
            return _ => throw new InvalidOperationException($"{typeof(TResponse).Name} Result tipi deyil; behavior onu qısa qapaya bilməz.");
        }

        var method = typeof(TResponse).GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static, [typeof(Error)])!;
        return error => (TResponse)method.Invoke(null, [error])!;
    }

    private static Func<object, TResponse>? BuildSuccess()
    {
        if (ValueType is null)
        {
            return null;
        }

        var method = typeof(TResponse).GetMethod(nameof(Result<int>.Success), BindingFlags.Public | BindingFlags.Static, [ValueType])!;
        return value => (TResponse)method.Invoke(null, [value])!;
    }
}
