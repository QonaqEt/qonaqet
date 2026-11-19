using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.GetCategoryForAdmin;

/// <summary>Admin: bir kateqoriya (deaktiv olsa da).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetCategoryForAdminQuery(CategoryId Id) : IQuery<CategoryDto>;

public sealed class GetCategoryForAdminHandler(ICategoryRepository categories) : IQueryHandler<GetCategoryForAdminQuery, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(GetCategoryForAdminQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var category = await categories.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return category is null ? CategoryErrors.NotFound : category.ToDto();
    }
}
