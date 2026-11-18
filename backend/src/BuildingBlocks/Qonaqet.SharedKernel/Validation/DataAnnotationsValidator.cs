using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Qonaqet.SharedKernel.Validation;

/// <summary>
/// Sadə DTO yoxlamaları üçün DataAnnotations adapteri (<c>[Required]</c>, <c>[MaxLength]</c>, <c>[Range]</c> …).
/// Atributlar sabit kodlara çevrilir, beləliklə xəta forması rule builder ilə eynidir.
/// </summary>
public sealed class DataAnnotationsValidator<T> : IValidator<T>
{
    private static readonly PropertyInfo[] Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && p.GetCustomAttributes<ValidationAttribute>(inherit: true).Any())
        .ToArray();

    public IReadOnlyList<ValidationFailure> Validate(T instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        foreach (var property in Properties)
        {
            var value = property.GetValue(instance);
            var context = new ValidationContext(instance) { MemberName = property.Name, DisplayName = property.Name };

            foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>(inherit: true))
            {
                if (attribute.GetValidationResult(value, context) is { } result && result != ValidationResult.Success)
                {
                    failures.Add(new ValidationFailure(CamelCase(property.Name), CodeFor(attribute), result.ErrorMessage ?? "Yanlış dəyər."));
                    break;
                }
            }
        }

        return failures;
    }

    private static string CodeFor(ValidationAttribute attribute) => attribute switch
    {
        RequiredAttribute => ValidationCodes.Required,
        MinLengthAttribute => ValidationCodes.TooShort,
        MaxLengthAttribute or StringLengthAttribute => ValidationCodes.TooLong,
        RangeAttribute => ValidationCodes.OutOfRange,
        EmailAddressAttribute => ValidationCodes.InvalidEmail,
        RegularExpressionAttribute => ValidationCodes.InvalidFormat,
        _ => ValidationCodes.Invalid,
    };

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
