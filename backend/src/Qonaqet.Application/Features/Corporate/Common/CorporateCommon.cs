using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using CorporateAggregate = Qonaqet.Domain.Corporate.Corporate;

namespace Qonaqet.Application.Features.Corporate.Common;

public sealed record CorporateDto(Guid Id, string Name, string Voen, Guid? LogoFileId, string? BrandColor, string ContactEmail);

public static class CorporateMapping
{
    public static CorporateDto ToDto(this CorporateAggregate corporate)
    {
        ArgumentNullException.ThrowIfNull(corporate);
        return new CorporateDto(corporate.Id.Value, corporate.Name, corporate.Voen, corporate.LogoFileId, corporate.BrandColor, corporate.ContactEmail);
    }
}

/// <summary>
/// Korporativ portalın girişi: cari istifadəçinin şirkəti DB-dən oxunur (JWT-dəki rol kifayət deyil — admin istifadəçini başqa şirkətə keçirə
/// bilər).
/// </summary>
public sealed class CorporateAccess(IUserRepository users, ICorporateRepository corporates, ICurrentUser currentUser)
{
    public async Task<Result<CorporateAggregate>> RequireAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user?.CorporateId is not { } corporateId || !user.HasRole(Role.CorporateUser))
        {
            return CorporateErrors.NoCorporate;
        }

        var corporate = await corporates.GetAsync(CorporateId.From(corporateId), cancellationToken).ConfigureAwait(false);
        return corporate is null ? CorporateErrors.NoCorporate : corporate;
    }
}
