using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Categories.Queries.ListCategories;

/// <summary>
/// <c>GET categories</c> (anonim): aktiv kateqoriyalar sıra üzrə. Mobil və veb bütün kateqoriyaya bağlı UI-ni (sihirbaz, filtrlər)
/// yalnız bundan qurur, ona görə admin yeni kateqoriya əlavə edəndə kod dəyişmədən görünür.
/// </summary>
[PublicRequest]
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;

public sealed class ListCategoriesHandler(ICategoryRepository categories) : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var items = await categories.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<CategoryDto>>.Success([.. items.Select(c => c.ToDto())]);
    }
}
