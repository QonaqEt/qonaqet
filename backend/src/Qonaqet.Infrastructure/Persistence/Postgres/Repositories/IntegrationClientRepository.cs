using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Identity;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>integration_clients</c> EF Core üzərində; ləğv olunmuşları qlobal soft-delete filtri gizlədir.</summary>
public sealed class IntegrationClientRepository(QonaqetDbContext context) : IIntegrationClientRepository
{
    public void Add(IntegrationClient client) => context.Set<IntegrationClient>().Add(client);

    public async Task<IntegrationClient?> GetAsync(IntegrationClientId id, CancellationToken cancellationToken) =>
        await context.Set<IntegrationClient>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<IntegrationClient?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken) =>
        await context.Set<IntegrationClient>().FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<IntegrationClient>> ListAsync(
        IntegrationOwnerType? ownerType, Guid? ownerId, int limit, CancellationToken cancellationToken)
    {
        var query = context.Set<IntegrationClient>().AsNoTracking();
        if (ownerType is { } type)
        {
            query = query.Where(c => c.OwnerType == type);
        }

        if (ownerId is { } owner)
        {
            query = query.Where(c => c.OwnerId == owner);
        }

        return await query.OrderByDescending(c => c.CreatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

/// <summary>İnteqrasiya platformasının DI qeydiyyatı.</summary>
public static class IntegrationRegistration
{
    public static IServiceCollection AddIntegration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IIntegrationClientRepository, IntegrationClientRepository>();
        services.AddScoped<PartnerAccess>();
        services.AddScoped<IPartnerFunding, PartnerFunding>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddSingleton<ISecretSealer, AesSecretSealer>();
        services.AddScoped<IWebhookSender, Integration.HttpWebhookSender>();
        services.AddHttpClient(Integration.HttpWebhookSender.ClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IPartnerTokenIssuer, JwtPartnerTokenIssuer>();
        return services;
    }
}
