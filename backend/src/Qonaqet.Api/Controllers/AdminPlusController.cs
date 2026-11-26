using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Plus.Commands.AdminBrands;
using Qonaqet.Application.Features.Plus.Commands.AdminCoupons;
using Qonaqet.Domain.Plus;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: QONAQET Plus brendləri, brend istifadəçiləri, kuponlar və unikal kodlar.</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminPlusController(ISender sender) : ControllerBase
{
    [HttpGet("brands")]
    public async Task<IActionResult> ListBrands(CancellationToken cancellationToken) =>
        (await sender.Send(new ListBrandsQuery(), cancellationToken)).ToActionResult();

    [HttpPost("brands")]
    public async Task<IActionResult> CreateBrand(BrandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CreateBrandCommand(request.ToDetails()), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("brands/{brandId:guid}")]
    public async Task<IActionResult> UpdateBrand(Guid brandId, BrandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new UpdateBrandCommand(BrandId.From(brandId), request.ToDetails()), cancellationToken)).ToActionResult();
    }

    /// <summary>Mövcud istifadəçini brendə bağlayır (<c>BrandUser</c> rolu).</summary>
    [HttpPost("brands/{brandId:guid}/users")]
    public async Task<IActionResult> AssignBrandUser(Guid brandId, BrandUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new AssignBrandUserCommand(BrandId.From(brandId), request.Email ?? string.Empty), cancellationToken)).ToActionResult();
    }

    [HttpGet("coupons")]
    public async Task<IActionResult> ListCoupons([FromQuery] Guid? brandId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListCouponsQuery(brandId is { } id ? BrandId.From(id) : null), cancellationToken)).ToActionResult();

    [HttpPost("coupons")]
    public async Task<IActionResult> CreateCoupon(CreateCouponRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateCouponCommand(BrandId.From(request.BrandId), request.CodeType, request.SharedCode, request.ToDetails()), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("coupons/{couponId:guid}")]
    public async Task<IActionResult> UpdateCoupon(Guid couponId, UpdateCouponRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new UpdateCouponCommand(CouponId.From(couponId), request.SharedCode, request.ToDetails()), cancellationToken))
            .ToActionResult();
    }

    /// <summary><c>Unique</c> kupona kodlar yükləyir (bir dəfəyə ≤ 5000).</summary>
    [HttpPost("coupons/{couponId:guid}/codes")]
    public async Task<IActionResult> AddCodes(Guid couponId, CouponCodesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new AddCouponCodesCommand(CouponId.From(couponId), request.Codes ?? []), cancellationToken)).ToActionResult();
    }

    public sealed record BrandRequest(string? Name, Guid? LogoFileId, string? Website, string? ContactEmail, bool IsActive = true)
    {
        public BrandDetails ToDetails() => new(Name ?? string.Empty, LogoFileId, string.IsNullOrWhiteSpace(Website) ? null : Website.Trim(), ContactEmail ?? string.Empty, IsActive);
    }

    public sealed record BrandUserRequest(string? Email);

    public sealed record CreateCouponRequest(
        Guid BrandId,
        CouponCodeType CodeType,
        string? SharedCode,
        string? Title,
        string? Description,
        Guid? ImageFileId,
        IReadOnlyList<Guid>? EligibleCategoryIds,
        int PerUserLimit,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidUntil,
        bool IsActive = true)
    {
        public CouponDetails ToDetails() =>
            new(Title ?? string.Empty, Description, ImageFileId, EligibleCategoryIds ?? [], PerUserLimit, ValidFrom.ToUniversalTime(), ValidUntil.ToUniversalTime(), IsActive);
    }

    public sealed record UpdateCouponRequest(
        string? SharedCode,
        string? Title,
        string? Description,
        Guid? ImageFileId,
        IReadOnlyList<Guid>? EligibleCategoryIds,
        int PerUserLimit,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidUntil,
        bool IsActive = true)
    {
        public CouponDetails ToDetails() =>
            new(Title ?? string.Empty, Description, ImageFileId, EligibleCategoryIds ?? [], PerUserLimit, ValidFrom.ToUniversalTime(), ValidUntil.ToUniversalTime(), IsActive);
    }

    public sealed record CouponCodesRequest(IReadOnlyList<string>? Codes);
}
