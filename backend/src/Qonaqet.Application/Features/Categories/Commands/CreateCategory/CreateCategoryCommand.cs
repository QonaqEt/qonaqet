using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Categories.Commands.CreateCategory;

/// <summary>
/// <c>POST admin/categories</c>: admin kod dəyişmədən yeni kateqoriya əlavə edir; mobil və veb onu dərhal <c>GET categories</c>-dən
/// görür. Kod silinməmiş kateqoriyalar arasında unikaldır.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateCategoryCommand(
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

public sealed class CreateCategoryValidator : CategoryValidator<CreateCategoryCommand>;

public sealed class CreateCategoryHandler(
    ICategoryRepository categories, CategoryIconCheck icons, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await categories.CodeExistsAsync(request.Code, cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.CodeTaken;
        }

        if (!await icons.IsUsableAsync(request.IconFileId, null, cancellationToken).ConfigureAwait(false))
        {
            return CategoryErrors.IconInvalid;
        }

        var now = clock.UtcNow;
        var category = Category.Create(request.Code, request.ToDetails(), now, currentUser.ActorId);
        await categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", CategoryAuditActions.Create, nameof(Category), category.Id.Value, CategoryAudit.Data(category))],
            cancellationToken).ConfigureAwait(false);
        return category.ToDto();
    }
}
