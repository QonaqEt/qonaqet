namespace Qonaqet.Application.Abstractions.Messaging;

/// <summary>
/// Autentifikasiya tələb etməyən sorğu (məs. OTP göndər, ictimai hədiyyə səhifəsi).
/// Atribut olmayan hər sorğu default olaraq autentifikasiya olunmuş aktor tələb edir.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PublicRequestAttribute : Attribute;

/// <summary>Sorğu üçün lazım olan rollardan ən azı biri (<c>User</c>, <c>BusinessOwner</c>, <c>Admin</c> …).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresRoleAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

/// <summary>
/// Pul hərəkət etdirən əmr: <c>Idempotency-Key</c> məcburidir. Eyni açar + eyni body → saxlanmış cavab
/// təkrar verilir; eyni açar + fərqli body → <c>409 idempotency_key_reused</c>.
/// </summary>
public interface IIdempotentCommand;

/// <summary>
/// Uğursuz nəticəsi də biznes nəticəsi olan əmr: təhlükəsizlik yan təsirləri (məs. oğurlanmış refresh token aşkarlananda
/// bütün sessiyaların bağlanması) xəta qaytarılsa da commit olunmalıdır. UnitOfWork belə əmrlərdə uğursuzluqda da
/// hadisələri outbox-a yazıb commit edir; exception isə yenə rollback edir.
/// </summary>
public interface ICommitsOnFailure;

/// <summary>
/// Yalnız aktiv (bloklanmamış, silinməmiş) hesab üçün: claim yaratmaq, hədiyyə göndərmək kimi əmrlər. Authorization
/// behavior hesabın vəziyyətini store-dan oxuyur; bloklanmış hesab <c>403 user_blocked</c> alır.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresActiveAccountAttribute : Attribute;
