using System.Linq.Expressions;
using System.Text;

namespace Qonaqet.SharedKernel.Validation;

/// <summary>Bir tipin yoxlayıcısı. Validation behavior hər command/query üçün bunları DI-dan alır.</summary>
public interface IValidator<in T>
{
    IReadOnlyList<ValidationFailure> Validate(T instance);
}

public static class ValidatorExtensions
{
    /// <summary>Uğur və ya bütün xətaları daşıyan <see cref="ValidationError"/>.</summary>
    public static Result ValidateToResult<T>(this IValidator<T> validator, T instance)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var failures = validator.Validate(instance);
        return failures.Count == 0 ? Result.Success() : Result.Failure(new ValidationError(failures));
    }
}

/// <summary>
/// Kiçik rule builder (FluentValidation yoxdur):
/// <code>RuleFor(x => x.Email).NotEmpty().MaxLength(254).Email();</code>
/// Sahə adı ifadədən camelCase yol kimi götürülür (<c>Recipient.Email</c> → <c>recipient.email</c>).
/// </summary>
public abstract class Validator<T> : IValidator<T>
{
    private readonly List<IRule> _rules = [];

    public IReadOnlyList<ValidationFailure> Validate(T instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        foreach (var rule in _rules)
        {
            rule.Validate(instance, failures);
        }

        return failures;
    }

    protected RuleBuilder<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var builder = new RuleBuilder<T, TProperty>(FieldPath(selector), selector.Compile());
        _rules.Add(builder);
        return builder;
    }

    /// <summary>Bütün obyekt üzərində sərbəst qayda (bir neçə sahədən asılı yoxlamalar üçün).</summary>
    protected void Rule(string field, Func<T, bool> isValid, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(isValid);
        _rules.Add(new ObjectRule(field, isValid, code, message));
    }

    private static string FieldPath(LambdaExpression selector)
    {
        var names = new Stack<string>();
        var expression = selector.Body;
        while (expression is MemberExpression member)
        {
            names.Push(member.Member.Name);
            expression = member.Expression;
        }

        if (expression is not ParameterExpression || names.Count == 0)
        {
            throw new ArgumentException("Selector sadə xüsusiyyət yolu olmalıdır (x => x.A.B).", nameof(selector));
        }

        return string.Join('.', names.Select(CamelCase));
    }

    internal static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        var sb = new StringBuilder(name);
        sb[0] = char.ToLowerInvariant(sb[0]);
        return sb.ToString();
    }

    internal interface IRule
    {
        void Validate(T instance, List<ValidationFailure> failures);
    }

    private sealed class ObjectRule(string field, Func<T, bool> isValid, string code, string message) : IRule
    {
        public void Validate(T instance, List<ValidationFailure> failures)
        {
            if (!isValid(instance))
            {
                failures.Add(new ValidationFailure(field, code, message));
            }
        }
    }
}

/// <summary>Bir sahənin ardıcıl yoxlamaları. Sahə üzrə ilk uğursuz yoxlamada dayanır.</summary>
public sealed class RuleBuilder<T, TProperty> : Validator<T>.IRule
{
    private readonly string _field;
    private readonly Func<T, TProperty> _getter;
    private readonly List<(Func<TProperty, bool> IsValid, string Code, string Message)> _checks = [];
    private Func<T, bool>? _condition;

    internal RuleBuilder(string field, Func<T, TProperty> getter)
    {
        _field = field;
        _getter = getter;
    }

    /// <summary>Qayda yalnız şərt doğru olanda yoxlanılır.</summary>
    public RuleBuilder<T, TProperty> When(Func<T, bool> condition)
    {
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        return this;
    }

    public RuleBuilder<T, TProperty> Must(Func<TProperty, bool> isValid, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(isValid);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _checks.Add((isValid, code, message));
        return this;
    }

    public RuleBuilder<T, TProperty> NotEmpty(string? message = null) =>
        Must(v => v switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            Guid g => g != Guid.Empty,
            System.Collections.ICollection c => c.Count > 0,
            _ => !EqualityComparer<TProperty>.Default.Equals(v, default!),
        }, ValidationCodes.Required, message ?? "Boş ola bilməz.");

    void Validator<T>.IRule.Validate(T instance, List<ValidationFailure> failures)
    {
        if (_condition is not null && !_condition(instance))
        {
            return;
        }

        var value = _getter(instance);
        foreach (var (isValid, code, message) in _checks)
        {
            if (!isValid(value))
            {
                failures.Add(new ValidationFailure(_field, code, message));
                return;
            }
        }
    }
}

/// <summary>Tez-tez işlənən yoxlamalar.</summary>
public static class RuleBuilderExtensions
{
    public static RuleBuilder<T, string?> MinLength<T>(this RuleBuilder<T, string?> rule, int min)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must(v => v is null || v.Length >= min, ValidationCodes.TooShort, $"Ən azı {min} simvol olmalıdır.");
    }

    public static RuleBuilder<T, string?> MaxLength<T>(this RuleBuilder<T, string?> rule, int max)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must(v => v is null || v.Length <= max, ValidationCodes.TooLong, $"Ən çox {max} simvol ola bilər.");
    }

    /// <summary>Sadə, sərt e-poçt yoxlaması: bir <c>@</c>, boşluqsuz, domen hissəsində nöqtə.</summary>
    public static RuleBuilder<T, string?> Email<T>(this RuleBuilder<T, string?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must(v => v is null || IsEmail(v), ValidationCodes.InvalidEmail, "E-poçt ünvanı düzgün deyil.");
    }

    public static RuleBuilder<T, TValue> InRange<T, TValue>(this RuleBuilder<T, TValue> rule, TValue min, TValue max)
        where TValue : IComparable<TValue>
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must(v => v.CompareTo(min) >= 0 && v.CompareTo(max) <= 0, ValidationCodes.OutOfRange, $"{min} ilə {max} arasında olmalıdır.");
    }

    public static RuleBuilder<T, string?> Matches<T>(this RuleBuilder<T, string?> rule, System.Text.RegularExpressions.Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(pattern);
        return rule.Must(v => v is null || pattern.IsMatch(v), ValidationCodes.InvalidFormat, "Format düzgün deyil.");
    }

    private static bool IsEmail(string value)
    {
        if (value.Length is < 3 or > 254 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1 && !domain.Contains("..", StringComparison.Ordinal);
    }
}
