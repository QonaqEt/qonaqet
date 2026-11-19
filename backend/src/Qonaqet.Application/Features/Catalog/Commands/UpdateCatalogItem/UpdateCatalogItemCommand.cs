using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Catalog.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Catalog.Commands.UpdateCatalogItem;

/// <summary>
/// <c>PUT business/{businessId}/catalog/{itemId}</c>: elementi tam əvəz edir. Element bu biznesə aid deyilsə tapılmır.
/// Hədiyyələr qiymət/başlıq snapshot-ı saxladığı üçün dəyişiklik artıq alınmış hədiyyələrə təsir etmir.
/// </summary>
[RequiresActiveAccount]
public sealed record UpdateCatalogItemCommand(
    BusinessId BusinessId,
    CatalogItemId ItemId,
    string Title,
    string? Description,
    long PriceQepik,
    Guid? ImageFileId,
    string? Group,
    bool IsActive,
    bool IsGiftable) : ICommand<CatalogItemDto>, ICatalogItemInput;

public sealed class UpdateCatalogItemValidator : CatalogItemValidator<UpdateCatalogItemCommand>;

public sealed class UpdateCatalogItemHandler(
    BusinessAccess access, ICatalogItemRepository items, BusinessImageCheck images, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateCatalogItemCommand, CatalogItemDto>
{
    public async Task<Result<CatalogItemDto>> Handle(UpdateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var item = await items.GetAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.BusinessId != request.BusinessId)
        {
            return CatalogErrors.NotFound;
        }

        if (!await images.IsUsableAsync(request.ImageFileId, FilePurpose.CatalogImage, item.ImageFileId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.ImageInvalid;
        }

        item.Update(request.ToDetails(), clock.UtcNow, currentUser.ActorId);
        await items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        return item.ToDto();
    }
}
