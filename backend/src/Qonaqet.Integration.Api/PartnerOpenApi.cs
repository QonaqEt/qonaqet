using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Qonaqet.Domain.Integration;

namespace Qonaqet.Integration.Api;

/// <summary>
/// "QONAQET Partner API v1" sənədinin əlavələri (E13-T10): OAuth2 client credentials sxemi (Postman/Insomnia import-da token avtomatik
/// alınır) və hər POST-da məcburi <c>Idempotency-Key</c> başlığı (<c>/oauth</c>-dan başqa).
/// </summary>
public static class PartnerOpenApi
{
    public const string DocumentName = "partner-v1";

    public const string SchemeName = "oauth2";

    public static IServiceCollection AddPartnerOpenApiDetails(this IServiceCollection services) =>
        services.Configure<OpenApiOptions>(DocumentName, o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Description =
                    "Partnyor inteqrasiyası: kod yoxlama və təhvil, geri alma, kataloq sinxronu, issuer hədiyyələri, balans, webhook-lar. "
                    + "Token: POST /oauth/token (client credentials). Hər POST-da Idempotency-Key məcburidir. Təlimat: docs/integration-guide.md.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description = "OAuth2 client credentials; token 1 saat etibarlıdır.",
                    Flows = new OpenApiOAuthFlows
                    {
                        ClientCredentials = new OpenApiOAuthFlow
                        {
                            TokenUrl = new Uri("/oauth/token", UriKind.Relative),
                            Scopes = PartnerScopes.All.ToDictionary(s => s, s => s, StringComparer.Ordinal),
                        },
                    },
                };
                document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }];
                return Task.CompletedTask;
            });

            o.AddOperationTransformer((operation, context, _) =>
            {
                var description = context.Description;
                var isTokenEndpoint = description.RelativePath?.StartsWith(Controllers.PartnerRoutes.OAuth, StringComparison.OrdinalIgnoreCase) == true;
                if (string.Equals(description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) && !isTokenEndpoint)
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "Idempotency-Key",
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Unikal açar (≤ 200 simvol). Eyni açar + eyni body → saxlanılmış cavab; fərqli body → 409.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = Infrastructure.Web.PartnerIdempotencyFilter.MaxKeyLength },
                    });
                }

                if (isTokenEndpoint)
                {
                    operation.Security = [];
                }

                return Task.CompletedTask;
            });
        });
}
