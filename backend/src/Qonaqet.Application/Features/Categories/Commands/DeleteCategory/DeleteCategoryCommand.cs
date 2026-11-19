using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Categories.Commands.DeleteCategory;

/// <summary>
/// <c>DELETE admin/categories/{id}</c>: soft delete. Aktiv təklifi, aktiv/qaralama kampaniyası, hovuz balansı və ya etibarlı
/// təsdiqi olan kateqoriya silinmir → <c>409 category_in_use</c> (səbəblər mesajdadır); belə kateqoriyanı yalnız deaktiv etmək
/// olar (<c>PUT</c> ilə <c>isActive: false</c>). Silinəndə boş hovuzları eyni tranzaksiyada kaskadla soft delete olunur.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record DeleteCategoryCommand(CategoryId Id, string Reason) : ICommand;

public sealed class DeleteCategoryValidator : Validator<DeleteCategoryCommand>
{
    public DeleteCategoryValidator()
    {
        RuleFor<string?>(x => x.Reason).NotEmpty().MaxLength(500);
    }
}

public sealed class DeleteCategoryHandler(
    ICategoryRepository categories, IPoolRepository pools, ICategoryUsageReader usage, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await categories.GetAsync(request.Id, cancellationToken).ConfigureAwait(false) is null)
        {
            return CategoryErrors.NotFound;
        }

        var blockers = await usage.GetDeleteBlockersAsync(request.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (blockers.Any)
        {
            return CategoryErrors.InUse(blockers.Reasons);
        }

        if (!await categories.SoftDeleteAsync(request.Id, currentUser.ActorId, request.Reason.Trim(), cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.NotFound;
        }

        await pools.CascadeDeleteForCategoryAsync(request.Id, currentUser.ActorId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
