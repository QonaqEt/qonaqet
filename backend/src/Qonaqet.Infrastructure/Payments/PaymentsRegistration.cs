using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Infrastructure.Persistence.Postgres.Repositories;
using Qonaqet.Infrastructure.Payments.Fake;
using Qonaqet.Infrastructure.Payments.Payriff;

namespace Qonaqet.Infrastructure.Payments;

/// <summary>Ödəniş provayderlərinin DI qeydiyyatı.</summary>
public static class PaymentsRegistration
{
    /// <summary><c>Payments:Provider</c>: <c>Payriff</c> və ya <c>Fake</c>; boşdursa Development-də açarsız Payriff → Fake.</summary>
    public const string ProviderKey = "Payments:Provider";

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

        services.AddScoped<FakePaymentGateway>();
        services.AddScoped<IFakeCheckout>(sp => sp.GetRequiredService<FakePaymentGateway>());
        services.AddScoped<IPaymentGatewayRegistry, PaymentGatewayRegistry>();
        services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<IPaymentGatewayRegistry>().Default);
        services.AddSingleton<IAppUrls, AppUrls>();

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<PaymentStarter>();
        services.AddScoped<PaymentRefunds>();
        services.AddScoped<PaymentConfirmation>();
        return services;
    }
}

/// <summary>
/// Default provayder: Development-də <c>Payments:Provider = Fake</c> və ya (provayder verilməyib və) <c>Payriff:AppKey</c> boşdursa Fake;
/// qalan bütün hallarda Payriff. Fake heç vaxt Development-dən kənarda default ola bilməz (konfiqurasiya xətası exception atır).
/// Sandbox həmişə Fake-dir.
/// </summary>
public sealed class PaymentGatewayRegistry(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment) : IPaymentGatewayRegistry
{
    public IPaymentGateway Default => UseFake(configuration, environment)
        ? services.GetRequiredService<FakePaymentGateway>()
        : services.GetRequiredService<PayriffGateway>();

    public IPaymentGateway Sandbox => services.GetRequiredService<FakePaymentGateway>();

    public IPaymentGateway Get(string providerName) => providerName switch
    {
        PayriffGateway.ProviderName => services.GetRequiredService<PayriffGateway>(),
        FakePaymentGateway.ProviderName => services.GetRequiredService<FakePaymentGateway>(),
        _ => throw new InvalidOperationException($"Naməlum ödəniş provayderi: {providerName}"),
    };

    public static bool UseFake(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var provider = configuration[PaymentsRegistration.ProviderKey];
        var wantsFake = string.Equals(provider, FakePaymentGateway.ProviderName, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(configuration[$"{PayriffOptions.SectionName}:AppKey"]) && environment.IsDevelopment());
        if (wantsFake && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Fake ödəniş gateway-i yalnız Development-də default ola bilər.");
        }

        return wantsFake;
    }
}

/// <summary><c>App:PublicBaseUrl</c> əsasında ictimai URL-lər (Payriff callback-i ictimai domenə gəlir).</summary>
public sealed class AppUrls(IConfiguration configuration) : IAppUrls
{
    public const string CallbackPath = "api/v1/payments/payriff/callback";

    public Uri PaymentCallback { get; } = new(FakePaymentGateway.BaseUrl(configuration), CallbackPath);
}
