using Qonaqet.Domain.Gifts;

namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Yeni link: açıq token (yalnız bir dəfə istifadəçiyə/email-ə gedir) və saxlanılan formaları.</summary>
public sealed record GiftLinkIssue(string Token, GiftLinkSecret Secret);

/// <summary>
/// Hədiyyə linkinin token-ləri. Token 32 təsadüfi bayt, base64url; bearer-dir və heç vaxt loglanmır. Saxlanılan: açarlı hash (axtarış) və
/// AES-GCM ilə şifrəli nüsxə (yalnız çatdırılma emaili və göndərənə ilk göstərilmə üçün, sonra aqreqat onu silir) — ADR 0007.
/// </summary>
public interface IGiftLinkTokens
{
    GiftLinkIssue Issue();

    /// <summary>Daxil edilmiş token-in hash-i; format səhvdirsə <c>null</c>.</summary>
    string? Hash(string? token);

    /// <summary>Şifrəli nüsxəni açır; pozulubsa <c>null</c>.</summary>
    string? Unseal(string? sealedToken);
}
