using Qonaqet.Application.Features.Campaigns.Jobs;
using Qonaqet.Application.Features.Corporate.Jobs;
using Qonaqet.Application.Features.Gifts.Jobs;
using Qonaqet.Application.Features.Leaderboard.Jobs;
using Qonaqet.Application.Features.Ledger.Jobs;
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

        // Gecəlik: hovuzların Mongo balansı ↔ ledger; fərqlər hesabata yazılır və xəbərdarlıq logu verilir.
        builder.Services.AddScheduledJob<ReconcileLedgerJob>(ReconcileLedgerJob.Name, ReconcileLedgerJob.Interval);
        builder.Services.AddScheduledJob<AwardPeriodBadgesJob>(AwardPeriodBadgesJob.Name, AwardPeriodBadgesJob.Interval);

        // Bitmiş kampaniyaları Ended edir, qalığı default hovuza köçürür (CampaignRollover).
        builder.Services.AddScheduledJob<EndCampaignsJob>(EndCampaignsJob.Name, EndCampaignsJob.Interval);

        // Vaxtı çatmış hədiyyələri çatdırır (temalı email + push); hər hədiyyə versiya şərti ilə bir dəfə.
        builder.Services.AddScheduledJob<DeliverScheduledGiftsJob>(DeliverScheduledGiftsJob.Name, DeliverScheduledGiftsJob.Interval);
        builder.Services.AddScheduledJob<Application.Features.Integration.Jobs.DispatchWebhooksJob>(
            Application.Features.Integration.Jobs.DispatchWebhooksJob.Name, Application.Features.Integration.Jobs.DispatchWebhooksJob.Interval);

        // Ləğv/müddət refundları provayder əlçatmaz olanda burada təkrarlanır (idempotent).
        builder.Services.AddScheduledJob<RetryGiftRefundsJob>(RetryGiftRefundsJob.Name, RetryGiftRefundsJob.Interval);

        // Müddəti bitən hədiyyələr: 7 gün qalmış xatırlatma, sonra qalığın refundu və ya hovuza bağışlanması.
        builder.Services.AddScheduledJob<ExpireGiftsJob>(ExpireGiftsJob.Name, ExpireGiftsJob.Interval);

        // Ödənilmiş korporativ sifarişlər üçün hər alıcıya hədiyyə (200-lük paketlər, idempotent, yarıda qalan yerdən davam edir).
        builder.Services.AddScheduledJob<CorporateFanOutJob>(CorporateFanOutJob.Name, CorporateFanOutJob.Interval);

        // Konkret işlər (payment re-check, gift delivery, claim expiry …) öz epic-ləri ilə AddScheduledJob<T>() ilə gəlir.
        builder.Services.AddHostedService<WorkerHeartbeat>();
        builder.Build().Run();
        return 0;
    }
}
