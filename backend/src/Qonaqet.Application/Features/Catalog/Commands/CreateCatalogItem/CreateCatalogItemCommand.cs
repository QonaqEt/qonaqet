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

namespace Qonaqet.Application.Features.Catalog.Commands.CreateCatalogItem;

/// <summary><c>POST business/{businessId}/catalog</c>: yalnız sahib (və admin) əlavə edir.</summary>
[RequiresActiveAccount]
public sealed record CreateCatalogItemCommand(
    BusinessId BusinessId,
    string Title,
    string? Description,
    long PriceQepik,
    Guid? ImageFileId,
    string? Group,
    bool IsActive = true,
    bool IsGiftable = true) : ICommand<CatalogItemDto>, ICatalogItemInput;

public sealed class CreateCatalogItemValidator : CatalogItemValidator<CreateCatalogItemCommand>;

public sealed class CreateCatalogItemHandler(
    BusinessAccess access, ICatalogItemRepository items, BusinessImageCheck images, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateCatalogItemCommand, CatalogItemDto>
{
    public async Task<Result<CatalogItemDto>> Handle(CreateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        if (!await images.IsUsableAsync(request.ImageFileId, FilePurpose.CatalogImage, null, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.ImageInvalid;
        }

        var item = CatalogItem.Create(request.BusinessId, request.ToDetails(), clock.UtcNow, currentUser.ActorId);
        await items.AddAsync(item, cancellationToken).ConfigureAwait(false);
        return item.ToDto();
    }
}
