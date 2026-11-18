using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Web;

/// <summary>Controller-lər üçün: <see cref="Result"/> → HTTP cavabı (controller-də məntiq olmasın deyə).</summary>
public static class ResultHttpExtensions
{
    public static IActionResult ToActionResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? new NoContentResult() : Problem(result.Error);
    }

    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result.Error);
    }

    private static ObjectResult Problem(Error error)
    {
        var problem = error.ToProblemDetails();
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError,
            ContentTypes = { "application/problem+json" },
        };
    }
}
