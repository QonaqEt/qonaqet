using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Behaviors;

/// <summary>Sorğu üçün qeydiyyatdan keçmiş bütün <see cref="IValidator{T}"/>-ləri işlədir; xəta varsa handler çağırılmır.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest>[] _validators = validators.ToArray();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (_validators.Length == 0)
        {
            return next(cancellationToken);
        }

        var failures = _validators.SelectMany(v => v.Validate(request)).ToArray();
        return failures.Length == 0
            ? next(cancellationToken)
            : Task.FromResult(ResultFactory<TResponse>.Failure(new ValidationError(failures)));
    }
}
