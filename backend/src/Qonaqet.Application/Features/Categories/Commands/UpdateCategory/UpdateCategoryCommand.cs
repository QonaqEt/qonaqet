using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Categories.Commands.UpdateCategory;

/// <summary>
/// <c>PUT admin/categories/{id}</c>: kateqoriyanı tam əvəz edir (deaktivasiya da buradandır: <c>isActive: false</c>).
/// Kateqoriya istifadədədirsə (təsdiq sorğusu, təsdiqlənmiş istifadəçi, təklif, kampaniya) kodu dəyişdirilə bilməz.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateCategoryCommand(
    CategoryId Id,
    string Code,
    string Name,
    string? Description,
    Guid? IconFileId,
    string Color,
    int SortOrder,
    bool IsActive,
    bool IsOpen,
    CategoryPrivacyLevel PrivacyLevel,
    VerificationConfigDto? Verification,
    CategoryLimitsDto Limits) : ICommand<CategoryDto>, ICategoryInput;

public sealed class UpdateCategoryValidator : CategoryValidator<UpdateCategoryCommand>;

public sealed class UpdateCategoryHandler(
    ICategoryRepository categories, ICategoryUsageReader usage, CategoryIconCheck icons, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var category = await categories.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return CategoryErrors.NotFound;
        }

        if (request.Code != category.Code)
        {
            if (await usage.IsInUseAsync(category.Id, cancellationToken).ConfigureAwait(false))
            {
                return CategoryErrors.CodeInUse;
            }

            if (await categories.CodeExistsAsync(request.Code, cancellationToken).ConfigureAwait(false))
            {
                return CategoryErrors.CodeTaken;
            }
        }

        if (!await icons.IsUsableAsync(request.IconFileId, category.IconFileId, cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.IconInvalid;
        }

        var now = clock.UtcNow;
        var previousCode = category.Code;
        category.ChangeCode(request.Code, now, currentUser.ActorId);
        category.Update(request.ToDetails(), now, currentUser.ActorId);
        await categories.UpdateAsync(category, cancellationToken).ConfigureAwait(false);

        var data = CategoryAudit.Data(category);
        if (previousCode != category.Code)
        {
            data["previousCode"] = previousCode;
        }

        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", CategoryAuditActions.Update, nameof(Category), category.Id.Value, data)],
            cancellationToken).ConfigureAwait(false);
        return category.ToDto();
    }
}
