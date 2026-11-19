using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Queries.GetCategoryForAdmin;
using Qonaqet.Application.Features.Admin.Queries.ListCategories;
using Qonaqet.Application.Features.Categories.Commands.CreateCategory;
using Qonaqet.Application.Features.Categories.Commands.DeleteCategory;
using Qonaqet.Application.Features.Categories.Commands.RestoreCategory;
using Qonaqet.Application.Features.Categories.Commands.UpdateCategory;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Application.Features.Categories.Queries.ListCategories;
using Qonaqet.Domain.Categories;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>İctimai kateqoriya siyahısı: mobil və veb kateqoriyaya bağlı bütün UI-ni bundan qurur.</summary>
[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new ListCategoriesQuery(), cancellationToken)).ToActionResult();
}

/// <summary>Admin: dinamik kateqoriyalar. Rol tələbi əmrlərin özündədir.</summary>
[ApiController]
[Route("api/v1/admin/categories")]
public sealed class AdminCategoriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeDeleted, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(new ListCategoriesForAdminQuery(includeDeleted, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCategoryForAdminQuery(CategoryId.From(id)), cancellationToken)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> Create(CategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateCategoryCommand(
                request.Code, request.Name, request.Description, request.IconFileId, request.Color, request.SortOrder, request.IsActive, request.IsOpen,
                request.PrivacyLevel, request.Verification, request.Limits),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(
            new UpdateCategoryCommand(
                CategoryId.From(id), request.Code, request.Name, request.Description, request.IconFileId, request.Color, request.SortOrder, request.IsActive,
                request.IsOpen, request.PrivacyLevel, request.Verification, request.Limits),
            cancellationToken)).ToActionResult();
    }

    /// <summary>Soft delete; istifadədə olan kateqoriya yalnız deaktiv edilə bilər (<c>409 category_in_use</c>).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteCategoryCommand(CategoryId.From(id), reason), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new RestoreCategoryCommand(CategoryId.From(id)), cancellationToken)).ToActionResult();

    public sealed record CategoryRequest(
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
        CategoryLimitsDto Limits);
}
