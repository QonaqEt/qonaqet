using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Categories.Commands.RestoreCategory;

/// <summary>
/// <c>POST admin/categories/{id}/restore</c>: silinmiş kateqoriyanı və kaskadla silinmiş hovuzlarını qaytarır. Bu arada eyni kodla
/// yeni kateqoriya yaradılıbsa bərpa olunmur (<c>category_code_taken</c>).
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RestoreCategoryCommand(CategoryId Id) : ICommand;

public sealed class RestoreCategoryHandler(ICategoryRepository categories, IPoolRepository pools, ICurrentUser currentUser)
    : ICommandHandler<RestoreCategoryCommand>
{
    public async Task<Result> Handle(RestoreCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var deleted = await categories.GetDeletedAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (deleted is null)
        {
            return CategoryErrors.NotFound;
        }

        if (await categories.CodeExistsAsync(deleted.Code, cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.CodeTaken;
        }

        if (!await categories.RestoreAsync(request.Id, currentUser.ActorId, cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.NotFound;
        }

        await pools.CascadeRestoreForCategoryAsync(request.Id, currentUser.ActorId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
