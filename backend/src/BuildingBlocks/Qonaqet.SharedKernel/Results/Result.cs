using System.Diagnostics.CodeAnalysis;

namespace Qonaqet.SharedKernel;

/// <summary>Dəyərsiz əməliyyatın nəticəsi: ya uğur, ya da bir <see cref="SharedKernel.Error"/>. Exception atmır.</summary>
public class Result
{
    private static readonly Result SuccessInstance = new(true, Error.None);

    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Uğurlu nəticə xəta daşıya bilməz.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Uğursuz nəticə xəta daşımalıdır.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => SuccessInstance;

    public static Result Failure(Error error) => new(false, error);

#pragma warning disable CA2225 // Adlı alternativ: Failure.
    public static implicit operator Result(Error error) => Failure(error);
#pragma warning restore CA2225
}

/// <summary>Dəyər qaytaran əməliyyatın nəticəsi.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>Uğurlu nəticənin dəyəri. Uğursuz nəticədə oxumaq proqramçı xətasıdır və exception atır.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Uğursuz nəticənin dəyəri yoxdur ({Error.Code}).");

    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = IsSuccess ? _value! : default;
        return IsSuccess;
    }

    public static Result<TValue> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<TValue>(value, true, Error.None);
    }

    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    public Result<TResult> Map<TResult>(Func<TValue, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TResult>.Success(map(_value!)) : Result<TResult>.Failure(Error);
    }

    public Result<TResult> Bind<TResult>(Func<TValue, Result<TResult>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(_value!) : Result<TResult>.Failure(Error);
    }

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

#pragma warning disable CA2225 // Adlı alternativlər: Success / Failure.
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);
#pragma warning restore CA2225
}
