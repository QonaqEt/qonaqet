using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Infrastructure.Messaging;

/// <summary>
/// Dispatcher-in DI qeydiyyatı: handler-lər assembly scan ilə tapılır və startup-da yoxlanılır —
/// handler-i olmayan və ya iki handler-i olan sorğu tətbiqin qalxmasına imkan vermir.
/// </summary>
public static class MessagingRegistration
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
        {
            throw new ArgumentException("Ən azı bir assembly verilməlidir.", nameof(assemblies));
        }

        return services.AddMessaging(assemblies.Distinct().SelectMany(LoadableTypes));
    }

    /// <summary>Verilən tiplər arasından handler və sorğuları qeydiyyatdan keçirir (assembly scan-in əsası).</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IEnumerable<Type> candidateTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidateTypes);

        var types = candidateTypes.Distinct().Where(t => t is { IsAbstract: false, IsInterface: false }).ToArray();

        var handlers = types
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Service: i, Implementation: t)))
            .ToArray();

        var duplicates = handlers
            .GroupBy(h => h.Service)
            .Where(g => g.Count() > 1)
            .Select(g => $"{RequestName(g.Key)}: {string.Join(", ", g.Select(h => h.Implementation.Name))}")
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Bir sorğunun bir neçə handler-i var — {string.Join("; ", duplicates)}");
        }

        var handled = handlers.Select(h => h.Service).ToHashSet();
        var missing = types
            .Where(t => !t.ContainsGenericParameters)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                .Select(i => (Request: t, Service: typeof(IRequestHandler<,>).MakeGenericType(t, i.GetGenericArguments()[0]))))
            .Where(r => !handled.Contains(r.Service))
            .Select(r => r.Request.FullName ?? r.Request.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Handler-i olmayan sorğular: {string.Join(", ", missing)}");
        }

        foreach (var (service, implementation) in handlers)
        {
            services.AddScoped(service, implementation);
        }

        services.AddScoped<ISender, Sender>();
        return services;
    }

    /// <summary>
    /// Açıq generik behavior əlavə edir (məs. <c>typeof(LoggingBehavior&lt;,&gt;)</c>). Çağırış sırası icra sırasıdır.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        var implementsBehavior = openBehaviorType.IsGenericTypeDefinition && openBehaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        if (!implementsBehavior)
        {
            throw new ArgumentException($"{openBehaviorType.Name} açıq generik IPipelineBehavior<,> olmalıdır.", nameof(openBehaviorType));
        }

        services.AddScoped(typeof(IPipelineBehavior<,>), openBehaviorType);
        return services;
    }

    private static string RequestName(Type handlerService) => handlerService.GetGenericArguments()[0].Name;

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
