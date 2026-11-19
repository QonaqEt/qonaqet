using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Files.Queries.GetFileContent;

/// <summary>
/// Faylın məzmunu <c>fileId</c> ilə. İctimai təyinatlar (loqo, şəkil) anonim oxunur; qalanları yalnız yükləyən və
/// admin üçün. Silinmiş, mövcud olmayan və icazəsiz fayl eyni cavabı alır: <c>404 file_not_found</c>.
/// </summary>
[PublicRequest]
public sealed record GetFileContentQuery(FileId FileId) : IQuery<FileContent>;

/// <summary>Çağıran (controller) <see cref="Content"/>-i cavaba yazıb bağlayır.</summary>
public sealed record FileContent(Stream Content, string ContentType, long SizeBytes, string Sha256, bool IsPublic);

public sealed class GetFileContentHandler(IStoredFileRepository files, IFileStorage storage, ICurrentUser currentUser)
    : IQueryHandler<GetFileContentQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetFileContentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var file = await files.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null || !FileAccessRules.CanRead(currentUser, file))
        {
            return FileErrors.NotFound;
        }

        var stored = await storage.OpenReadAsync(file.ObjectKey, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return FileErrors.NotFound;
        }

        return new FileContent(stored.Content, file.ContentType, file.SizeBytes, file.Sha256, file.IsPublic);
    }
}
