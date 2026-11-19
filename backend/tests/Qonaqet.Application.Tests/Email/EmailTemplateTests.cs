using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Email;

namespace Qonaqet.Application.Tests.Email;

public sealed class EmailTemplateTests
{
    [Fact]
    public void Placeholder_values_are_html_encoded()
    {
        var html = TemplateEngine.Render("<p>{{ name }}</p>", _ => "<script>alert('x')</script> & \"ə\"", htmlEncode: true);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_placeholder_value_is_an_error() =>
        Assert.Throws<KeyNotFoundException>(() => TemplateEngine.Render("{{unknown}}", _ => null, htmlEncode: true));

    [Fact]
    public void Design_tokens_are_flattened_and_references_resolved()
    {
        var tokens = DesignTokens.Flatten();

        Assert.Equal("#E8475F", tokens["color.brand.nar"]);
        Assert.Equal(tokens["color.brand.saffron"], tokens["color.theme.birthday"]);
        Assert.Equal("Manrope", tokens["font.family.ui"]);
    }

    [Fact]
    public void Otp_template_renders_azerbaijani_text_brand_colors_and_the_code()
    {
        var email = new EmailTemplateRenderer().Render("otp", new Dictionary<string, string?>
        {
            ["code"] = "048213",
            ["validMinutes"] = "10",
            ["expiresAtBaku"] = "14:35",
        });

        Assert.Equal("QONAQET giriş kodunuz: 048213", email.Subject);
        Assert.Contains("048213", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("xoşbəxt", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("#E8475F", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Bakı vaxtı", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_send_does_not_throw_to_the_caller_and_is_retried_until_it_succeeds()
    {
        var sender = new FlakySender(failures: 2);
        var queue = new ChannelEmailQueue();
        var options = Options.Create(new EmailOptions { RetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)] });
        using var dispatcher = new EmailDispatcher(queue, sender, options, NullLogger<EmailDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync(new EmailMessage("a@example.az", "Salam", "<p>Salam</p>", "Salam"), "test", CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (sender.Delivered == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await dispatcher.StopAsync(CancellationToken.None);
        Assert.Equal(3, sender.Attempts);
        Assert.Equal(1, sender.Delivered);
    }

    [Fact]
    public async Task Email_is_given_up_after_the_configured_retries()
    {
        var sender = new FlakySender(failures: int.MaxValue);
        var queue = new ChannelEmailQueue();
        var options = Options.Create(new EmailOptions { RetryDelays = [TimeSpan.FromMilliseconds(5)] });
        using var dispatcher = new EmailDispatcher(queue, sender, options, NullLogger<EmailDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync(new EmailMessage("a@example.az", "Salam", "<p>Salam</p>", "Salam"), "test", CancellationToken.None);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(2, sender.Attempts);
        Assert.Equal(0, sender.Delivered);
    }

    private sealed class FlakySender(int failures) : IEmailSender
    {
        private int _attempts;

        public int Attempts => _attempts;

        public int Delivered { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) <= failures)
            {
                throw new IOException("SMTP əlçatan deyil");
            }

            Delivered++;
            return Task.CompletedTask;
        }
    }
}

public sealed class PdfTableTests
{
    [Fact]
    public void Table_cells_and_headers_are_html_encoded()
    {
        var html = Infrastructure.Documents.Pdf.PlaywrightPdfRenderer.TableHtml(
            new PdfTable(["<th>"], [["<b>qalın</b> & ş"]]));

        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;qalın&lt;/b&gt; &amp; ş", html, StringComparison.Ordinal);
        Assert.Contains("&lt;th&gt;", html, StringComparison.Ordinal);
    }
}
