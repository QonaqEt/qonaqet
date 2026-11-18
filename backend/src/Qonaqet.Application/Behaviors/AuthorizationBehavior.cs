using System.Collections.Concurrent;
using System.Reflection;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// Sorğu səviyyəli icazə: default olaraq autentifikasiya tələb olunur (<see cref="PublicRequestAttribute"/> istisnadır),
/// <see cref="RequiresRoleAttribute"/> rollardan birini tələb edir. Resurs yoxlaması ("bu staff bu biznesə aiddir")
/// handler-dədir.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, Policy> Policies = new();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var policy = Policies.GetOrAdd(typeof(TRequest), Policy.For);

        if (policy.IsPublic)
        {
            return next(cancellationToken);
        }

        if (!currentUser.IsAuthenticated)
        {
            return Task.FromResult(ResultFactory<TResponse>.Failure(CommonErrors.Unauthorized));
        }

        if (policy.Roles.Count > 0 && !policy.Roles.Any(currentUser.Roles.Contains))
        {
            return Task.FromResult(ResultFactory<TResponse>.Failure(CommonErrors.Forbidden));
        }

        return next(cancellationToken);
    }

    private sealed record Policy(bool IsPublic, IReadOnlyList<string> Roles)
    {
        public static Policy For(Type requestType) => new(
            requestType.GetCustomAttribute<PublicRequestAttribute>() is not null,
            requestType.GetCustomAttribute<RequiresRoleAttribute>()?.Roles ?? []);
    }
}
