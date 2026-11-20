using Qonaqet.Application.Features.Payments.Jobs;
using Qonaqet.Application.Features.Social.Jobs;
using Qonaqet.Application.Features.Verification.Jobs;
using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Documents.Pdf;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.Infrastructure.Jobs;
using Qonaqet.Infrastructure.Messaging;

namespace Qonaqet.Worker;

public static class Program
{
    public static int Main(string[] args)
    {
        if (HealthProbe.IsRequested(args))
        {
            return HealthProbe.RunHeartbeat(DateTimeOffset.UtcNow);
        }

        // Docker build zamanı (Worker stage): Chromium headless shell + Linux asılılıqları.
        if (args.Contains("--install-chromium", StringComparer.Ordinal))
        {
            return PdfRegistration.InstallChromium(withDependencies: true);
        }

        var builder = Host.CreateApplicationBuilder(args);

        builder.AddQonaqetObservability("qonaqet-worker");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddMessagingHost(builder.Configuration);
        builder.Services.AddPdfRenderer(builder.Configuration);

        builder.Services.AddJobScheduler();

        // Saatda bir yoxlayır; hər təsdiq üçün xatırlatma bir dəfə gedir (idempotent), ona görə tez-tez işləmək təhlükəsizdir.
        builder.Services.AddScheduledJob<VerificationRemindersJob>(VerificationRemindersJob.Name, TimeSpan.FromHours(1));

        // 30 dəqiqəlik claim rezervlərini buraxır; hər claim atomik status keçidi ilə götürülür.
        builder.Services.AddScheduledJob<ExpireClaimsJob>(ExpireClaimsJob.Name, TimeSpan.FromMinutes(1));

        // Callback itsə belə ödənişləri Get Order ilə təsdiqləyir, 30 dəqiqədən köhnə ödənilməmişləri Expired edir.
        builder.Services.AddScheduledJob<RecheckPaymentsJob>(RecheckPaymentsJob.Name, RecheckPaymentsJob.Interval);

        // Konkret işlər (payment re-check, gift delivery, claim expiry …) öz epic-ləri ilə AddScheduledJob<T>() ilə gəlir.
        builder.Services.AddHostedService<WorkerHeartbeat>();
        builder.Build().Run();
        return 0;
    }
}
