using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Corporate.Commands.CorporateReports;
using Qonaqet.Domain.Corporate;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// PDF hesabatlarının consumer-i ("corporate-reports", Worker): <see cref="CorporateReportRequested"/> → <see cref="CorporateReportRenderer"/>
/// (Playwright yalnız Worker-də qeydiyyatdadır, ona görə renderer burada yaradılır). Renderer yalnız <c>Pending</c> sorğunu emal edir.
/// </summary>
public sealed class CorporateReportsConsumer(IServiceScopeFactory scopes) : IIntegrationEventConsumer
{
    public static readonly string ReportRequestedType = typeof(CorporateReportRequested).FullName!;

    public string Name => "corporate-reports";

    public IReadOnlyCollection<string> EventTypes { get; } = [ReportRequestedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var scope = scopes.CreateAsyncScope();
        var renderer = ActivatorUtilities.CreateInstance<CorporateReportRenderer>(scope.ServiceProvider);
        await renderer.RenderAsync(CorporateReportId.From(SocialEventsConsumer.GuidField(message, "reportId")), cancellationToken).ConfigureAwait(false);
    }
}
