using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Plus.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Plus.Commands.AdminCoupons;

/// <summary>
/// <c>POST admin/coupons</c>: admin brend üçün kupon yaradır. <c>Shared</c> kuponun kodu özündədir; <c>Unique</c> kuponun kodları ayrıca yüklənir
/// (<see cref="AddCouponCodesCommand"/>). Kateqoriyalar yalnız id ilə seçilir; boş siyahı = hamıya açıq.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateCouponCommand(BrandId BrandId, CouponCodeType CodeType, string? SharedCode, CouponDetails Details) : ICommand<CouponDto>;

/// <summary><c>PUT admin/coupons/{id}</c>: sahələr və aktivlik; kod növü dəyişmir, ortaq kod dəyişə bilər.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateCouponCommand(CouponId CouponId, string? SharedCode, CouponDetails Details) : ICommand<CouponDto>;

/// <summary>
/// <c>POST admin/coupons/{id}/codes</c>: <c>Unique</c> kupona kodlar (bir dəfəyə ≤ 5000); artıq olanlar atlanır. Tranzaksiyasızdır: təkrar kod
/// unikal indeksdə rədd olunur, bu isə tranzaksiyanı ləğv edərdi.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record AddCouponCodesCommand(CouponId CouponId, IReadOnlyList<string> Codes) : ICommand<CouponCodeImport>, INonTransactionalCommand;

/// <summary><c>GET admin/coupons?brandId=</c>: kuponlar və kod sayları.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListCouponsQuery(BrandId? BrandId) : IQuery<IReadOnlyList<CouponDto>>;

public sealed class CreateCouponValidator : Validator<CreateCouponCommand>
{
    public CreateCouponValidator()
    {
        Rule("details", x => Coupon.IsValid(x.Details), PlusErrors.CouponInvalid.Code, PlusErrors.CouponInvalid.Message);
        Rule(
            "sharedCode",
            x => x.CodeType == CouponCodeType.Shared ? CouponCodes.Normalize(x.SharedCode) is not null : x.SharedCode is null,
            PlusErrors.CouponCodeInvalid.Code,
            PlusErrors.CouponCodeInvalid.Message);
    }
}

public sealed class UpdateCouponValidator : Validator<UpdateCouponCommand>
{
    public UpdateCouponValidator()
    {
        Rule("details", x => Coupon.IsValid(x.Details), PlusErrors.CouponInvalid.Code, PlusErrors.CouponInvalid.Message);
        Rule("sharedCode", x => x.SharedCode is null || CouponCodes.Normalize(x.SharedCode) is not null, PlusErrors.CouponCodeInvalid.Code, PlusErrors.CouponCodeInvalid.Message);
    }
}

public sealed class AddCouponCodesValidator : Validator<AddCouponCodesCommand>
{
    public const int MaxCodes = 5000;

    public AddCouponCodesValidator()
    {
        Rule("codes", x => x.Codes is { Count: > 0 and <= MaxCodes }, PlusErrors.TooManyCodes.Code, PlusErrors.TooManyCodes.Message);
        Rule("codes", x => x.Codes is null || x.Codes.All(c => CouponCodes.Normalize(c) is not null), PlusErrors.CouponCodeInvalid.Code, PlusErrors.CouponCodeInvalid.Message);
    }
}

/// <summary>Kuponun seçdiyi kateqoriyalar mövcud olmalıdır (silinmiş və ya naməlum id qəbul olunmur).</summary>
public sealed class CouponCategories(ICategoryRepository categories)
{
    public async Task<bool> AllExistAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids.Distinct())
        {
            if (await categories.GetAsync(CategoryId.From(id), cancellationToken).ConfigureAwait(false) is null)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class CreateCouponHandler(
    IBrandRepository brands, ICouponRepository coupons, CouponCategories categories, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<CreateCouponCommand, CouponDto>
{
    public async Task<Result<CouponDto>> Handle(CreateCouponCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await brands.GetAsync(request.BrandId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PlusErrors.BrandNotFound;
        }

        if (!await categories.AllExistAsync(request.Details.EligibleCategoryIds, cancellationToken).ConfigureAwait(false))
        {
            return PlusErrors.CategoryNotFound;
        }

        var now = clock.UtcNow;
        var coupon = Coupon.Create(request.BrandId, request.CodeType, request.SharedCode, request.Details, now, currentUser.ActorId);
        await coupons.AddAsync(coupon, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.CouponCreate, nameof(Coupon), coupon.Id.Value)], cancellationToken)
            .ConfigureAwait(false);
        return coupon.ToDto(new CouponCodeCounts(0, 0, 0));
    }
}

public sealed class UpdateCouponHandler(
    ICouponRepository coupons, ICouponCodeRepository codes, CouponCategories categories, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<UpdateCouponCommand, CouponDto>
{
    public async Task<Result<CouponDto>> Handle(UpdateCouponCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var coupon = await coupons.GetAsync(request.CouponId, cancellationToken).ConfigureAwait(false);
        if (coupon is null)
        {
            return PlusErrors.CouponNotFound;
        }

        if (request.SharedCode is not null && coupon.CodeType != CouponCodeType.Shared)
        {
            return PlusErrors.CouponCodeInvalid;
        }

        if (!await categories.AllExistAsync(request.Details.EligibleCategoryIds, cancellationToken).ConfigureAwait(false))
        {
            return PlusErrors.CategoryNotFound;
        }

        var now = clock.UtcNow;
        coupon.Update(request.Details, request.SharedCode, now, currentUser.ActorId);
        await coupons.UpdateAsync(coupon, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.CouponUpdate, nameof(Coupon), coupon.Id.Value)], cancellationToken)
            .ConfigureAwait(false);
        return coupon.ToDto(await codes.CountsAsync(coupon.Id, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class AddCouponCodesHandler(ICouponRepository coupons, ICouponCodeRepository codes, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<AddCouponCodesCommand, CouponCodeImport>
{
    public async Task<Result<CouponCodeImport>> Handle(AddCouponCodesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var coupon = await coupons.GetAsync(request.CouponId, cancellationToken).ConfigureAwait(false);
        if (coupon is null)
        {
            return PlusErrors.CouponNotFound;
        }

        if (coupon.CodeType != CouponCodeType.Unique)
        {
            return PlusErrors.CodesNotUnique;
        }

        var now = clock.UtcNow;
        var normalized = request.Codes.Select(c => CouponCodes.Normalize(c)!).ToList();
        var distinct = normalized.Distinct(StringComparer.Ordinal).ToList();
        var import = await codes.AddCodesAsync([.. distinct.Select(c => CouponCode.Create(coupon.Id, c, now, currentUser.ActorId))], cancellationToken)
            .ConfigureAwait(false);
        var result = import with { Duplicates = import.Duplicates + (normalized.Count - distinct.Count) };
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.CouponCodesAdd, nameof(Coupon), coupon.Id.Value,
                new Dictionary<string, string?> { ["added"] = result.Added.ToString(System.Globalization.CultureInfo.InvariantCulture) })],
            cancellationToken).ConfigureAwait(false);
        return result;
    }
}

public sealed class ListCouponsHandler(ICouponRepository coupons, ICouponCodeRepository codes) : IQueryHandler<ListCouponsQuery, IReadOnlyList<CouponDto>>
{
    public async Task<Result<IReadOnlyList<CouponDto>>> Handle(ListCouponsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await coupons.ListAsync(request.BrandId, 500, cancellationToken).ConfigureAwait(false);
        var result = new List<CouponDto>(list.Count);
        foreach (var coupon in list)
        {
            result.Add(coupon.ToDto(await codes.CountsAsync(coupon.Id, cancellationToken).ConfigureAwait(false)));
        }

        return result;
    }
}
