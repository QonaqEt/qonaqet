using System.Collections.Concurrent;

namespace Qonaqet.SharedKernel.Messaging;

/// <summary>
/// Öz CQRS dispatcher-imiz (MediatR yoxdur). Handler və behavior-ları <see cref="IServiceProvider"/>-dən
/// alır; hər sorğu tipi üçün generik "wrapper" bir dəfə yaradılıb keşlənir, sonrakı çağırışlarda
/// refleksiya yoxdur.
/// </summary>
public sealed class Sender(IServiceProvider serviceProvider) : ISender
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd(
            request.GetType(),
            static type => Activator.CreateInstance(typeof(RequestWrapper<,>).MakeGenericType(type, typeof(TResponse)))!);

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    private abstract class RequestWrapper<TResponse>
    {
        public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestWrapper<TRequest, TResponse> : RequestWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typed = (TRequest)request;

            var handler = services.GetService(typeof(IRequestHandler<TRequest, TResponse>)) as IRequestHandler<TRequest, TResponse>
                ?? throw new InvalidOperationException($"{typeof(TRequest).Name} üçün handler qeydiyyatdan keçməyib.");

            var behaviors = services.GetService(typeof(IEnumerable<IPipelineBehavior<TRequest, TResponse>>))
                as IEnumerable<IPipelineBehavior<TRequest, TResponse>> ?? [];

            RequestHandlerDelegate<TResponse> pipeline = ct => handler.Handle(typed, ct);

            // İlk qeydiyyat ən xarici olsun deyə tərsinə bükülür.
            foreach (var behavior in behaviors.Reverse())
            {
                var next = pipeline;
                pipeline = ct => behavior.Handle(typed, next, ct);
            }

            return pipeline(cancellationToken);
        }
    }
}
