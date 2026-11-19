using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.ListCategories;

/// <summary>Admin: bütün kateqoriyalar (deaktivlər də) sıra üzrə; <c>includeDeleted</c> silinmişləri də göstərir.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListCategoriesForAdminQuery(bool IncludeDeleted, int? Page, int? PageSize) : IQuery<PagedResult<CategoryDto>>;

public sealed class ListCategoriesForAdminHandler(ICategoryRepository categories) : IQueryHandler<ListCategoriesForAdminQuery, PagedResult<CategoryDto>>
{
    public async Task<Result<PagedResult<CategoryDto>>> Handle(ListCategoriesForAdminQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await categories.ListForAdminAsync(request.IncludeDeleted, PageRequest.Of(request.Page, request.PageSize), cancellationToken).ConfigureAwait(false);
        return new PagedResult<CategoryDto>([.. page.Items.Select(c => c.ToDto())], page.Total);
    }
}
