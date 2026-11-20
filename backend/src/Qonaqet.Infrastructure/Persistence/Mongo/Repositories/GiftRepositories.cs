using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Gifts.Common;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public static class GiftCollectionNames
{
    public const string Gifts = "gifts";
    public const string GiftRedemptions = "giftRedemptions";
}

/// <summary>Hədiyyə sahəsinin DI qeydiyyatı.</summary>
public static class GiftRegistration
{
    public static IServiceCollection AddGifts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<GiftPricing>();
        return services;
    }
}
