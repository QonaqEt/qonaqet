using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Catalog.Commands.DeleteCatalogItem;

/// <summary><c>DELETE business/{businessId}/catalog/{itemId}</c>: həmişə soft delete (audit log ilə); sənəd qalır.</summary>
[RequiresActiveAccount]
public sealed record DeleteCatalogItemCommand(BusinessId BusinessId, CatalogItemId ItemId, string? Reason) : ICommand;

public sealed class DeleteCatalogItemValidator : Validator<DeleteCatalogItemCommand>
{
    public DeleteCatalogItemValidator()
    {
        RuleFor(x => x.Reason).MaxLength(500);
    }
}

public sealed class DeleteCatalogItemHandler(BusinessAccess access, ICatalogItemRepository items, ICurrentUser currentUser) : ICommandHandler<DeleteCatalogItemCommand>
{
    public async Task<Result> Handle(DeleteCatalogItemCommand request, CancellationToken cancellationToken)
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

        await items.SoftDeleteAsync(item.Id, currentUser.ActorId, request.Reason ?? "catalog item deleted", cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
