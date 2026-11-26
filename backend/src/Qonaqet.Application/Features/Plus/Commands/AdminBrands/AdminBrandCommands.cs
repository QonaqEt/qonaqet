using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Plus.Common;
using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Plus.Commands.AdminBrands;

/// <summary><c>POST admin/brands</c>: admin QONAQET Plus brendi yaradır. Audit log-a düşür.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateBrandCommand(BrandDetails Details) : ICommand<BrandDto>;

/// <summary><c>PUT admin/brands/{id}</c>: profil və aktivlik (deaktiv brendin kuponları istifadəçilərə görünmür).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateBrandCommand(BrandId BrandId, BrandDetails Details) : ICommand<BrandDto>;

/// <summary>
/// <c>POST admin/brands/{id}/users</c>: email ilə mövcud istifadəçini brendə bağlayır və ona <c>BrandUser</c> rolu verir (yeni rol növbəti token
/// yeniləməsində JWT-yə düşür). Başqa brendə bağlı istifadəçi bu brendə keçir.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record AssignBrandUserCommand(BrandId BrandId, string Email) : ICommand<BrandUserDto>;

/// <summary><c>GET admin/brands</c>.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListBrandsQuery : IQuery<IReadOnlyList<BrandDto>>;

public sealed record BrandUserDto(Guid UserId, Guid BrandId);

public sealed class CreateBrandValidator : Validator<CreateBrandCommand>
{
    public CreateBrandValidator() =>
        Rule("details", x => Brand.IsValid(x.Details), PlusErrors.BrandInvalid.Code, PlusErrors.BrandInvalid.Message);
}

public sealed class UpdateBrandValidator : Validator<UpdateBrandCommand>
{
    public UpdateBrandValidator() =>
        Rule("details", x => Brand.IsValid(x.Details), PlusErrors.BrandInvalid.Code, PlusErrors.BrandInvalid.Message);
}

public sealed class CreateBrandHandler(IBrandRepository brands, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<CreateBrandCommand, BrandDto>
{
    public async Task<Result<BrandDto>> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var brand = Brand.Create(request.Details, now, currentUser.ActorId);
        await brands.AddAsync(brand, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.BrandCreate, nameof(Brand), brand.Id.Value)], cancellationToken)
            .ConfigureAwait(false);
        return brand.ToDto();
    }
}

public sealed class UpdateBrandHandler(IBrandRepository brands, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<UpdateBrandCommand, BrandDto>
{
    public async Task<Result<BrandDto>> Handle(UpdateBrandCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brand = await brands.GetAsync(request.BrandId, cancellationToken).ConfigureAwait(false);
        if (brand is null)
        {
            return PlusErrors.BrandNotFound;
        }

        var now = clock.UtcNow;
        brand.Update(request.Details, now, currentUser.ActorId);
        await brands.UpdateAsync(brand, cancellationToken).ConfigureAwait(false);
        await outbox.AddAsync(
            [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.BrandUpdate, nameof(Brand), brand.Id.Value)], cancellationToken)
            .ConfigureAwait(false);
        return brand.ToDto();
    }
}

public sealed class AssignBrandUserHandler(IBrandRepository brands, IUserRepository users, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<AssignBrandUserCommand, BrandUserDto>
{
    public async Task<Result<BrandUserDto>> Handle(AssignBrandUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brand = await brands.GetAsync(request.BrandId, cancellationToken).ConfigureAwait(false);
        if (brand is null)
        {
            return PlusErrors.BrandNotFound;
        }

        var user = await users.GetByEmailAsync((request.Email ?? string.Empty).Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return PlusErrors.UserNotFound;
        }

        var now = clock.UtcNow;
        if (user.AssignBrand(brand.Id.Value, now, currentUser.ActorId))
        {
            await users.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
            await outbox.AddAsync(
                [new AuditRecorded(now, currentUser.ActorId, "User", PlusAuditActions.BrandAssignUser, nameof(Brand), brand.Id.Value,
                    new Dictionary<string, string?> { ["userId"] = user.Id.Value.ToString() })],
                cancellationToken).ConfigureAwait(false);
        }

        return new BrandUserDto(user.Id.Value, brand.Id.Value);
    }
}

public sealed class ListBrandsHandler(IBrandRepository brands) : IQueryHandler<ListBrandsQuery, IReadOnlyList<BrandDto>>
{
    public async Task<Result<IReadOnlyList<BrandDto>>> Handle(ListBrandsQuery request, CancellationToken cancellationToken) =>
        (await brands.ListAsync(500, cancellationToken).ConfigureAwait(false)).Select(b => b.ToDto()).ToList();
}
