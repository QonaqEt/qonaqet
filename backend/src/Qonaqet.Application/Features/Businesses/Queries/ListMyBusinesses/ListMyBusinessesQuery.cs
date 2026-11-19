using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Businesses.Queries.ListMyBusinesses;

/// <summary>
/// <c>GET business/mine</c>: biznes dəyişdiricisi üçün aktorun bütün bizneslərı (şəbəkə) və rolu, üstəgəl hələ baxılmamış
/// müraciətləri. Panel endpoint-ləri biznesi URL-də açıq tələb edir; klient onu bu siyahıdan seçir, server "default" seçmir.
/// </summary>
public sealed record ListMyBusinessesQuery : IQuery<MyBusinessesDto>;

public sealed record MyBusinessDto(BusinessId BusinessId, string Name, string Slug, BusinessType Type, BusinessStatus Status, MembershipRole? Role, Guid? LogoFileId);

/// <summary><see cref="Businesses"/>: üzvü olduğu bizneslər; <see cref="Applications"/>: aktivləşdirilməmiş müraciətlər (rolsuz).</summary>
public sealed record MyBusinessesDto(IReadOnlyList<MyBusinessDto> Businesses, IReadOnlyList<MyBusinessDto> Applications);

public sealed class ListMyBusinessesHandler(IBusinessRepository businesses, IBusinessMembershipRepository memberships, ICurrentUser currentUser)
    : IQueryHandler<ListMyBusinessesQuery, MyBusinessesDto>
{
    public async Task<Result<MyBusinessesDto>> Handle(ListMyBusinessesQuery request, CancellationToken cancellationToken)
    {
        var actor = currentUser.ActorId!.Value;
        var mine = new List<MyBusinessDto>();
        foreach (var membership in await memberships.ListForUserAsync(actor, cancellationToken).ConfigureAwait(false))
        {
            if (await businesses.GetAsync(membership.BusinessId, cancellationToken).ConfigureAwait(false) is { } business)
            {
                mine.Add(ToDto(business, membership.Role));
            }
        }

        var memberOf = mine.Select(m => m.BusinessId).ToHashSet();
        var applications = (await businesses.ListByApplicantAsync(actor, cancellationToken).ConfigureAwait(false))
            .Where(b => !memberOf.Contains(b.Id))
            .Select(b => ToDto(b, null))
            .ToList();
        return new MyBusinessesDto(mine, applications);
    }

    private static MyBusinessDto ToDto(Business business, MembershipRole? role) =>
        new(business.Id, business.Name, business.Slug, business.Type, business.Status, role, business.LogoFileId);
}
