using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Commands.IssuePartnerToken;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>OAuth2 token endpoint-i (client credentials). Body <c>application/x-www-form-urlencoded</c> (RFC 6749) və ya JSON.</summary>
[ApiController]
[Route(PartnerRoutes.OAuth + "/token")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class OAuthController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> TokenForm([FromForm] TokenRequest request, CancellationToken cancellationToken) =>
        await IssueAsync(request, cancellationToken);

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> TokenJson([FromBody] TokenRequest request, CancellationToken cancellationToken) =>
        await IssueAsync(request, cancellationToken);

    private async Task<IActionResult> IssueAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new IssuePartnerTokenCommand(request.GrantType, request.ClientId, request.ClientSecret, request.Scope), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(new TokenResponse(result.Value.AccessToken, result.Value.TokenType, result.Value.ExpiresIn, result.Value.Scope));
    }

    /// <summary>RFC 6749 sahə adları (snake_case).</summary>
    public sealed class TokenRequest
    {
        [FromForm(Name = "grant_type")]
        [System.Text.Json.Serialization.JsonPropertyName("grant_type")]
        public string? GrantType { get; set; }

        [FromForm(Name = "client_id")]
        [System.Text.Json.Serialization.JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [FromForm(Name = "client_secret")]
        [System.Text.Json.Serialization.JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [FromForm(Name = "scope")]
        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    public sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string Scope);
}
