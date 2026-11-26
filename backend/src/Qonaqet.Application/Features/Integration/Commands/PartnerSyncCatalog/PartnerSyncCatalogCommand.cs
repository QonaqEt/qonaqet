using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerSyncCatalog;

/// <summary>
/// <c>PUT /partner/v1/catalog/{businessId}</c> (<c>catalog:write</c>): POS menyusunu <c>externalId</c> üzrə toplu upsert edir (bir Mongo
/// tranzaksiyasında, ≤ 1000 məhsul). Dəyişməyən məhsul yazılmır. <paramref name="Prune"/> = <c>true</c> olduqda sorğuda olmayan və əvvəl
/// sinxronla gəlmiş (externalId-li) məhsullar soft delete olunur; əl ilə yaradılmış məhsullara heç vaxt toxunulmur. <see cref="CatalogSynced"/>
/// hadisəsi <c>catalog.synced</c> webhook-unu qidalandırır.
/// </summary>
[RequiresRole(PartnerScopes.CatalogWrite)]
public sealed record PartnerSyncCatalogCommand(Guid BusinessId, IReadOnlyList<PartnerCatalogItem> Items, bool Prune) : ICommand<CatalogSyncDto>;

public sealed record PartnerCatalogItem(
    string ExternalId, string Title, string? Description, long PriceQepik, string? Group, bool IsActive = true, bool IsGiftable = true);

public sealed record CatalogSyncDto(int Created, int Updated, int Unchanged, int Pruned);

public sealed class PartnerSyncCatalogValidator : Validator<PartnerSyncCatalogCommand>
{
    public const int MaxItems = 1000;

    public PartnerSyncCatalogValidator()
    {
        Rule("items", x => x.Items is { Count: <= MaxItems }, "catalog_too_large", $"Bir sinxronda ən çox {MaxItems} məhsul ola bilər.");
        Rule(
            "items",
            x => x.Items is null || x.Items.Select(i => i.ExternalId).Distinct(StringComparer.Ordinal).Count() == x.Items.Count,
            "catalog_duplicate_external_id",
            "externalId sorğu daxilində təkrarlanır.");
        Rule("items", x => x.Items is null || x.Items.All(IsValid), "catalog_item_invalid", "Məhsul sahələri (externalId, ad, qiymət) düzgün deyil.");
    }

    private static bool IsValid(PartnerCatalogItem item) =>
        item is not null
        && CatalogItem.IsValidExternalId(item.ExternalId)
        && !string.IsNullOrWhiteSpace(item.Title) && item.Title.Trim().Length <= CatalogItem.TitleMaxLength
        && (item.Description is null || item.Description.Length <= CatalogItem.DescriptionMaxLength)
        && (item.Group is null || item.Group.Length <= CatalogItem.GroupMaxLength)
        && CatalogItem.IsValidPrice(item.PriceQepik);
}

public sealed class PartnerSyncCatalogHandler(PartnerAccess access, ICatalogItemRepository catalog, IOutbox outbox, IClock clock)
    : ICommandHandler<PartnerSyncCatalogCommand, CatalogSyncDto>
{
    public const string PruneReason = "catalog_sync_prune";

    public async Task<Result<CatalogSyncDto>> Handle(PartnerSyncCatalogCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var granted = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (granted.IsFailure)
        {
            return granted.Error;
        }

        var (client, business) = granted.Value;
        var now = clock.UtcNow;
        var actor = client.Id.Value;
        var existing = (await catalog.ListForBusinessAsync(business.Id, activeOnly: false, cancellationToken).ConfigureAwait(false))
            .Where(i => i.ExternalId is not null).ToDictionary(i => i.ExternalId!, StringComparer.Ordinal);

        int created = 0, updated = 0, unchanged = 0, pruned = 0;
        foreach (var item in request.Items)
        {
            var details = new CatalogItemDetails(item.Title, item.Description, item.PriceQepik, null, item.Group, item.IsActive, item.IsGiftable);
            if (existing.Remove(item.ExternalId, out var current))
            {
                if (!current.Differs(details with { ImageFileId = current.ImageFileId }))
                {
                    unchanged++;
                    continue;
                }

                current.Update(details with { ImageFileId = current.ImageFileId }, now, actor);
                await catalog.UpdateAsync(current, cancellationToken).ConfigureAwait(false);
                updated++;
            }
            else
            {
                await catalog.AddAsync(CatalogItem.Create(business.Id, details, now, actor, item.ExternalId), cancellationToken).ConfigureAwait(false);
                created++;
            }
        }

        if (request.Prune)
        {
            foreach (var missing in existing.Values)
            {
                if (await catalog.SoftDeleteAsync(missing.Id, actor, PruneReason, cancellationToken).ConfigureAwait(false))
                {
                    pruned++;
                }
            }
        }

        await outbox.AddAsync([new CatalogSynced(business.Id, actor, created, updated, unchanged, pruned, now)], cancellationToken).ConfigureAwait(false);
        return new CatalogSyncDto(created, updated, unchanged, pruned);
    }
}
