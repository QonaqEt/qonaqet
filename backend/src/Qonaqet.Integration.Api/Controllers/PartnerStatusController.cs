using Microsoft.AspNetCore.Mvc;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>
/// <c>GET /partner/v1/status</c>: Partner API-nin əlçatanlığı və versiyası (auth tələb etmir, heç bir məlumat qaytarmır). Bütün partnyor
/// endpoint-ləri <see cref="PartnerRoutes.Prefix"/> altındadır; yalnız OAuth token endpoint-i <c>/oauth</c>-dadır.
/// </summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/status")]
public sealed class PartnerStatusController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new PartnerStatus("QONAQET Partner API", PartnerRoutes.Version));

    public sealed record PartnerStatus(string Name, string Version);
}

/// <summary>Partner API-nin marşrut prefiksi.</summary>
public static class PartnerRoutes
{
    public const string Version = "v1";

    public const string Prefix = "partner/" + Version;

    /// <summary>OAuth2 token endpoint-i (prefiksdən kənarda, standart yer).</summary>
    public const string OAuth = "oauth";
}
