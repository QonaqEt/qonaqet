using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Verification.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Users;
using Qonaqet.Domain.Verification;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.ListVerifications;

/// <summary>Admin: təsdiq növbəsi (<c>?status=Pending</c>, <c>?categoryId=</c>), ən köhnələr əvvəl.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListVerificationsQuery(VerificationStatus? Status, CategoryId? CategoryId, int? Page, int? PageSize) : IQuery<PagedResult<AdminVerificationDto>>;

/// <summary>Admin: bir sorğu (sahələr, sənəd ID-ləri, sahibin email-i).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetVerificationForAdminQuery(VerificationRequestId Id) : IQuery<AdminVerificationDto>;

public sealed class ListVerificationsHandler(IVerificationRequestRepository requests, ICategoryRepository categories, IUserRepository users)
    : IQueryHandler<ListVerificationsQuery, PagedResult<AdminVerificationDto>>
{
    public async Task<Result<PagedResult<AdminVerificationDto>>> Handle(ListVerificationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await requests.ListForAdminAsync(request.Status, request.CategoryId, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);
        var items = new List<AdminVerificationDto>(page.Items.Count);
        var categoryCache = new Dictionary<CategoryId, Category?>();
        foreach (var item in page.Items)
        {
            if (!categoryCache.TryGetValue(item.CategoryId, out var category))
            {
                category = await categories.GetAsync(item.CategoryId, cancellationToken).ConfigureAwait(false);
                categoryCache[item.CategoryId] = category;
            }

            var user = await users.GetAsync(UserId.From(item.UserId), cancellationToken).ConfigureAwait(false);
            items.Add(item.ToAdminDto(category, user));
        }

        return new PagedResult<AdminVerificationDto>(items, page.Total);
    }
}

public sealed class GetVerificationForAdminHandler(IVerificationRequestRepository requests, ICategoryRepository categories, IUserRepository users)
    : IQueryHandler<GetVerificationForAdminQuery, AdminVerificationDto>
{
    public async Task<Result<AdminVerificationDto>> Handle(GetVerificationForAdminQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await requests.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return VerificationErrors.NotFound;
        }

        var category = await categories.GetAsync(item.CategoryId, cancellationToken).ConfigureAwait(false);
        var user = await users.GetAsync(UserId.From(item.UserId), cancellationToken).ConfigureAwait(false);
        return item.ToAdminDto(category, user);
    }
}
