using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Files.Commands.UploadFile;

/// <summary>
/// Cari istifadəçinin adına fayl yükləyir (sahib = istifadəçi). Sonrakı əmrlər (loqo, şəkil, CSV) faylı yalnız
/// <c>fileId</c> ilə istinad edir və onu yükləyənin özü olduğunu yoxlayır.
/// </summary>
[RequiresActiveAccount]
public sealed record UploadFileCommand(FilePurpose Purpose, string ContentType, ReadOnlyMemory<byte> Content) : ICommand<UploadedFileDto>;

public sealed record UploadedFileDto(Guid FileId, string Purpose, string ContentType, long SizeBytes);

public sealed class UploadFileHandler(FileUploader uploader, ICurrentUser currentUser) : ICommandHandler<UploadFileCommand, UploadedFileDto>
{
    public async Task<Result<UploadedFileDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId!.Value;
        var result = await uploader.UploadAsync(
            new FileUpload(request.Purpose, request.ContentType, request.Content), FileOwnerType.User, userId, userId, cancellationToken).ConfigureAwait(false);
        return result.Map(f => new UploadedFileDto(f.Id.Value, f.Purpose.ToString(), f.ContentType, f.SizeBytes));
    }
}
