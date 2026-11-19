using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Verification.Commands.SubmitVerification;
using Qonaqet.Application.Features.Verification.Queries.GetMyVerifications;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Files;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Api.Controllers;

/// <summary>Kateqoriya təsdiqi (status sihirbazı). Sahələr və tələblər <c>GET categories</c>-dəki konfiqurasiyadan gəlir.</summary>
[ApiController]
[Route("api/v1/verification")]
public sealed class VerificationController(ISender sender) : ControllerBase
{
    /// <summary>Ən çox 10 sənəd × 5 MB + form sahələri üçün ehtiyat.</summary>
    private const long RequestLimitBytes = (VerificationConfig.MaxFilesLimit * FilePolicy.FiveMegabytes) + (512 * 1024);

    /// <summary>
    /// Multipart: <c>categoryId</c>, <c>subtype</c> (alt növ kodu), <c>fields</c> (JSON obyekt: açar → dəyər), <c>fin</c>, <c>files</c> (bir neçə).
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Submit(
        [FromForm] Guid categoryId, [FromForm] string? subtype, [FromForm] string? fields, [FromForm] string? fin, [FromForm] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string>? answers = null;
        if (!string.IsNullOrWhiteSpace(fields))
        {
            try
            {
                answers = JsonSerializer.Deserialize<Dictionary<string, string>>(fields);
            }
            catch (JsonException)
            {
                return Result.Failure(new ValidationError([new ValidationFailure("fields", ValidationCodes.InvalidFormat, "Sahələr JSON obyekt (açar → mətn) olmalıdır.")]))
                    .ToActionResult();
            }
        }

        var documents = new List<VerificationDocumentUpload>();
        foreach (var file in files ?? [])
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            documents.Add(new VerificationDocumentUpload(file.ContentType, buffer.ToArray()));
        }

        var result = await sender.Send(new SubmitVerificationCommand(CategoryId.From(categoryId), subtype, answers, fin, documents), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyVerificationsQuery(), cancellationToken)).ToActionResult();
}
