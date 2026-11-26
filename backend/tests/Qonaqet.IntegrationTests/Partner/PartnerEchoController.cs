using Microsoft.AspNetCore.Mvc;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>Yalnız testlər üçün partner POST-u: hər icrada yeni id — təkrar cavabın saxlanmış olduğunu göstərir.</summary>
[ApiController]
[Route("partner/v1/__echo")]
public sealed class PartnerEchoController : ControllerBase
{
    private static int _executions;

    public static int Executions => _executions;

    [HttpPost]
    public IActionResult Post(EchoRequest request) =>
        StatusCode(201, new { id = Guid.CreateVersion7(), value = request?.Value, execution = Interlocked.Increment(ref _executions) });

    public sealed record EchoRequest(string? Value);
}
