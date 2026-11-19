using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Files;

namespace Qonaqet.Application.Features.Files.Common;

/// <summary>Fayla giriş qaydaları: ictimai təyinatlar hamıya, qalanları yalnız yükləyənə və adminə.</summary>
public static class FileAccessRules
{
    public static bool CanRead(ICurrentUser user, StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(file);
        return file.IsPublic || CanManage(user, file);
    }

    public static bool CanManage(ICurrentUser user, StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(file);
        return user.IsAuthenticated && (user.IsAdmin() || (file.CreatedBy is { } uploader && uploader == user.ActorId));
    }
}
