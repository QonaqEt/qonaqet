using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Payments.Payriff;

namespace Qonaqet.Infrastructure.Payments;

/// <summary>Ödəniş provayderlərinin DI qeydiyyatı.</summary>
public static class PaymentsRegistration
{
    public static IServiceCollection AddPayments(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PayriffOptions>(configuration.GetSection(PayriffOptions.SectionName));
        services.AddHttpClient<PayriffGateway>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PayriffOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.Timeout = Timeout.InfiniteTimeSpan; // taymautları resilience pipeline idarə edir
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions o, IServiceProvider sp) =>
            {
                var options = sp.GetRequiredService<IOptions<PayriffOptions>>().Value;
                o.AttemptTimeout.Timeout = options.AttemptTimeout;
                o.TotalRequestTimeout.Timeout = options.TotalTimeout;
                o.Retry.Delay = options.RetryDelay;
                o.CircuitBreaker.SamplingDuration = options.AttemptTimeout * 2 > TimeSpan.FromSeconds(30) ? options.AttemptTimeout * 2 : TimeSpan.FromSeconds(30);
                // Sifariş yaratma və refund (POST) təkrarlanmır: ikiqat pul hərəkəti olmasın.
                o.Retry.DisableForUnsafeHttpMethods();
            });

        services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<PayriffGateway>());
        return services;
    }
}
