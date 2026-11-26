using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Plus.Common;

/// <summary>
/// Brend bölməsinin girişi: cari istifadəçinin brendi DB-dən oxunur (JWT-dəki rol kifayət deyil — admin istifadəçini başqa brendə keçirə bilər).
/// Başqa brendin kuponu və kodu heç vaxt görünmür.
/// </summary>
public sealed class BrandAccess(IUserRepository users, IBrandRepository brands, ICurrentUser currentUser)
{
    public async Task<Result<Brand>> RequireAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user?.BrandId is not { } brandId || !user.HasRole(Role.BrandUser))
        {
            return PlusErrors.NoBrand;
        }

        var brand = await brands.GetAsync(BrandId.From(brandId), cancellationToken).ConfigureAwait(false);
        return brand is null ? PlusErrors.NoBrand : brand;
    }
}
