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
