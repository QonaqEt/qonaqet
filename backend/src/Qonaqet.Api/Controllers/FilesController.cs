using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Files.Commands.DeleteFile;
using Qonaqet.Application.Features.Files.Commands.UploadFile;
using Qonaqet.Application.Features.Files.Queries.GetFileContent;
using Qonaqet.Domain.Files;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Fayl yükləmə və oxuma. Fayllara yalnız <c>fileId</c> ilə müraciət olunur; obyekt açarı heç vaxt çıxmır.</summary>
[ApiController]
[Route("api/v1/files")]
public sealed class FilesController(ISender sender) : ControllerBase
{
    /// <summary>Multipart sorğunun ümumi limiti: 5 MB fayl + form sahələri üçün ehtiyat.</summary>
    private const long RequestLimitBytes = FilePolicy.FiveMegabytes + (256 * 1024);

    [HttpPost]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] FilePurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[] content;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await sender.Send(new UploadFileCommand(purpose, file.ContentType, content), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetFileContentQuery(FileId.From(id)), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var content = result.Value;
        Response.Headers.CacheControl = content.IsPublic ? "public, max-age=86400, immutable" : "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Content, content.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{content.Sha256}\""), lastModified: null);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteFileCommand(FileId.From(id), reason), cancellationToken)).ToActionResult();
}
