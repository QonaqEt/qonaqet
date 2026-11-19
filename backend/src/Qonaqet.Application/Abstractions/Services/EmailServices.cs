namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Göndəriləcək email (HTML + düz mətn alternativ). Mətnlər UTF-8-dir.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Şablondan alınmış email məzmunu.</summary>
public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody)
{
    public EmailMessage To(string address) => new(address, Subject, HtmlBody, TextBody);
}

/// <summary>SMTP göndərici (MailKit). Uğursuzluqda exception atır; biznes axınları birbaşa deyil, <see cref="IEmailQueue"/> ilə göndərir.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Öz <c>{{placeholder}}</c> şablon mühərrikimiz: dəyərlər HTML-encode olunur (XSS yoxdur), brend rəngləri
/// <c>design-tokens/tokens.json</c>-dan gəlir. Şablon adı: <c>otp</c>, sonra <c>gift-delivered</c> və s.
/// </summary>
public interface IEmailTemplateRenderer
{
    RenderedEmail Render(string templateName, IReadOnlyDictionary<string, string?> model);
}

/// <summary>
/// Email növbəsi: göndərmə uğursuzluğu çağıran əməliyyatı qırmır, arxa planda artan fasilələrlə yenidən cəhd olunur.
/// Növbə proses daxilindədir (gizli dəyərlər — məs. OTP — heç yerdə saxlanılmır); davamlılıq tələb edən axınlar
/// (hədiyyə çatdırılması) öz statusları ilə yenidən göndərir.
/// </summary>
public interface IEmailQueue
{
    /// <summary><paramref name="kind"/> loglar üçün email növüdür (ünvan və məzmun loglanmır).</summary>
    ValueTask EnqueueAsync(EmailMessage message, string kind, CancellationToken cancellationToken);
}
