namespace Qonaqet.SharedKernel.Messaging;

/// <summary>Dispatcher-in göndərə bildiyi hər sorğu. Birbaşa deyil, <see cref="ICommand"/> / <see cref="IQuery{TResponse}"/> ilə işlədilir.</summary>
public interface IRequest<TResponse>;

/// <summary>Bütün əmrlərin qeyri-generik marker-i (UnitOfWork yalnız əmrlərə tətbiq olunur).</summary>
public interface ICommandBase;

/// <summary>Bütün oxuma sorğularının qeyri-generik marker-i.</summary>
public interface IQueryBase;

/// <summary>Vəziyyəti dəyişən, dəyər qaytarmayan əmr.</summary>
public interface ICommand : IRequest<Result>, ICommandBase;

/// <summary>Vəziyyəti dəyişən və dəyər (adətən yeni ID) qaytaran əmr.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ICommandBase;

/// <summary>Vəziyyəti dəyişməyən oxuma sorğusu.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>, IQueryBase;

/// <summary>Bir sorğu tipinin yeganə handler-i. Birbaşa deyil, aşağıdakı ixtisaslaşmış interfeyslərlə implement olunur.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>Növbəti addımı (növbəti behavior və ya handler) çağıran delegat.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Handler-in ətrafında dekorator. Qeydiyyat sırası icra sırasıdır: ilk qeyd olunan ən xaricidir
/// (Logging → Validation → Authorization → Idempotency → UnitOfWork → handler).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Sorğunu handler-inə (behavior-lar üzərindən) çatdırır.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
