using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qonaqet.Infrastructure.Observability;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Infrastructure.Web;

/// <summary>Hər iki HTTP host-un (Api, Integration.Api) ortaq veb konfiqurasiyası.</summary>
public static class WebHostExtensions
{
    /// <summary>Controller-lər, JSON, ProblemDetails, exception handler və OpenAPI sənədi.</summary>
    public static IServiceCollection AddQonaqetWeb(this IServiceCollection services, string documentName, string documentTitle)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new StronglyTypedIdJsonConverterFactory()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = InvalidModelState);

        services.AddHttpContextAccessor();
        services.AddScoped<Application.Abstractions.Services.ICurrentUser, HttpCurrentUser>();
        services.AddScoped<Application.Abstractions.Services.IRequestContext, HttpRequestContext>();
        services.AddQonaqetRateLimits();

        services.AddProblemDetails();
        services.AddExceptionHandler<UnhandledExceptionHandler>();

        services.AddOpenApi(documentName, o => o.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = documentTitle;
            document.Info.Version = "v1";
            return Task.CompletedTask;
        }));

        return services;
    }

    /// <summary>Exception handler ən əvvəl; OpenAPI sənədi yalnız Development-də açılır.</summary>
    public static WebApplication UseQonaqetWeb(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        // Liveness: proses cavab verir (store yoxlanmır). Readiness: bütün store-lar.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(StoreHealthChecks.ReadyTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        app.UseRateLimiter();
        app.MapControllers();
        return app;
    }

    /// <summary>Model binding xətaları da öz formatımızla: <c>validation_failed</c> + <c>errors[]</c>.</summary>
    private static IActionResult InvalidModelState(ActionContext context)
    {
        var failures = context.ModelState
            .Where(e => e.Value is { Errors.Count: > 0 })
            .Select(e => new ValidationFailure(CamelPath(e.Key), ValidationCodes.Invalid, "Dəyər oxuna bilmədi və ya düzgün deyil."))
            .ToArray();

        Error error = failures.Length > 0
            ? new ValidationError(failures)
            : Error.Validation(ValidationCodes.ValidationFailed, "Sorğu yoxlamadan keçmədi.");

        return new ObjectResult(error.ToProblemDetails())
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static string CamelPath(string key) =>
        string.Join('.', key.TrimStart('$', '.').Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Length == 0 || char.IsLower(p[0]) ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
