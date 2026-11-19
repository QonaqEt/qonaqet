using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Files.Commands.DeleteFile;

/// <summary>
/// Faylın soft delete-i (audit log ilə): metadata <c>isDeleted</c> olur, fayl artıq heç yerdən verilmir; obyekt isə
/// saxlamada qalır (açar dəyişməz, heç nə silinmir). Yalnız yükləyən və ya admin. Başqasının faylı üçün 404 —
/// mövcudluğu sezdirmirik.
/// </summary>
public sealed record DeleteFileCommand(FileId FileId, string? Reason = null) : ICommand;

public sealed class DeleteFileHandler(IStoredFileRepository files, ICurrentUser currentUser) : ICommandHandler<DeleteFileCommand>
{
    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var file = await files.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null || !FileAccessRules.CanManage(currentUser, file))
        {
            return FileErrors.NotFound;
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Deleted by owner" : request.Reason.Trim();
        await files.SoftDeleteAsync(file.Id, currentUser.ActorId, reason[..Math.Min(reason.Length, 500)], cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
