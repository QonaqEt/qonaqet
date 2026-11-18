namespace Qonaqet.SharedKernel.Validation;

/// <summary>Bir sahənin yoxlama xətası: JSON sahə yolu (<c>recipient.email</c>), sabit kod və mesaj.</summary>
public sealed record ValidationFailure(string Field, string Code, string Message);

/// <summary>Standart validasiya xəta kodları. API müqaviləsinin hissəsidir.</summary>
public static class ValidationCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string Required = "required";
    public const string TooShort = "too_short";
    public const string TooLong = "too_long";
    public const string OutOfRange = "out_of_range";
    public const string InvalidEmail = "invalid_email";
    public const string InvalidFormat = "invalid_format";
    public const string Invalid = "invalid";
}

/// <summary>Bir və ya bir neçə sahə xətasını daşıyan <see cref="Error"/> (<c>validation_failed</c>).</summary>
public sealed record ValidationError : Error
{
    public ValidationError(IReadOnlyList<ValidationFailure> failures)
        : base(ValidationCodes.ValidationFailed, "Sorğu yoxlamadan keçmədi.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException("Ən azı bir xəta olmalıdır.", nameof(failures));
        }

        Failures = failures;
    }

    public IReadOnlyList<ValidationFailure> Failures { get; }

    public bool Equals(ValidationError? other) =>
        other is not null && base.Equals(other) && Failures.SequenceEqual(other.Failures);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Failures.Count);
}
