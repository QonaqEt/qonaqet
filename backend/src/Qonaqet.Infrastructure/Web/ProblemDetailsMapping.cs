using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Infrastructure.Web;

/// <summary>
/// <see cref="Error"/> → RFC 9457 ProblemDetails. Sabit kod <c>code</c> genişlənməsindədir,
/// validasiya xətaları <c>errors: [{ field, code, message }]</c> kimi qaytarılır.
/// Hər iki HTTP host (Api, Integration.Api) bunu işlədir.
/// </summary>
public static class ProblemDetailsMapping
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    public static ProblemDetails ToProblemDetails(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error == Error.None)
        {
            throw new ArgumentException("Uğurlu nəticə ProblemDetails-ə çevrilmir.", nameof(error));
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodeFor(error.Type),
            Title = error.Message,
            Type = $"https://qonaqet.az/problems/{error.Code}",
        };
        problem.Extensions["code"] = error.Code;

        if (error is ValidationError validation)
        {
            problem.Extensions["errors"] = validation.Failures
                .Select(f => new FieldProblem(f.Field, f.Code, f.Message))
                .ToArray();
        }

        return problem;
    }

    /// <summary>JSON-da <c>{ "field", "code", "message" }</c>.</summary>
    public sealed record FieldProblem(string Field, string Code, string Message);
}
