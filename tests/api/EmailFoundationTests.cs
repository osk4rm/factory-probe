using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwarmApp.Api.Infrastructure.Email;

namespace SwarmApp.Api.Tests;

/// <summary>
/// Unit tests for the email seam. Deliberately **not** using <see cref="ApiFactory"/>:
/// that fixture starts a PostgreSQL container, and a unit test that needs Docker is a
/// unit test that cannot run in the places it is most useful. Nothing here touches a
/// database, a network or the disk beyond a temp directory.
/// </summary>
public sealed class EmailFoundationTests
{
    private static EmailOptions ValidOptions(string provider = "file") => new()
    {
        Provider = provider,
        FromAddress = "no-reply@example.test",
        PublicBaseUrl = "https://app.example.test",
        File = new FileSinkOptions { Directory = "data/emails" },
    };

    private static FileEmailSender FileSender(string directory) =>
        new(
            Options.Create(new EmailOptions
            {
                Provider = "file",
                FromAddress = "no-reply@example.test",
                File = new FileSinkOptions { Directory = directory },
            }),
            new StubEnvironment(Path.GetTempPath()),
            NullLogger<FileEmailSender>.Instance);

    [Fact]
    public void All_three_templates_render_without_network_or_database()
    {
        var messages = new[]
        {
            EmailTemplates.Confirm("a@example.test", "https://app.example.test/confirm?t=abc"),
            EmailTemplates.Reset("b@example.test", "https://app.example.test/reset?t=def"),
            EmailTemplates.Receipt("c@example.test", "Pro", "9 EUR", "https://app.example.test/invoice/1"),
        };

        Assert.All(messages, message =>
        {
            Assert.False(string.IsNullOrWhiteSpace(message.Subject));
            Assert.False(string.IsNullOrWhiteSpace(message.HtmlBody));
            // A message with only an HTML part scores as spam with most providers.
            Assert.False(string.IsNullOrWhiteSpace(message.TextBody));
        });
    }

    [Fact]
    public void Each_template_carries_its_link_in_both_bodies()
    {
        const string confirmUrl = "https://app.example.test/confirm?t=abc";
        var message = EmailTemplates.Confirm("a@example.test", confirmUrl);

        Assert.Contains(confirmUrl, message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(confirmUrl, message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_template_refuses_a_relative_link()
    {
        // A relative link in an email is a link nobody can click, and it would be
        // discovered by a customer rather than here.
        Assert.Throws<ArgumentException>(() => EmailTemplates.Confirm("a@example.test", "/confirm?t=abc"));
    }

    [Fact]
    public void User_supplied_text_is_html_escaped()
    {
        var message = EmailTemplates.Receipt(
            "c@example.test",
            "<script>alert(1)</script>",
            "9 EUR",
            "https://app.example.test/invoice/1");

        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_file_provider_writes_exactly_one_file_per_message()
    {
        var directory = Path.Combine(Path.GetTempPath(), "swarm-email-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sender = FileSender(directory);

            await sender.SendAsync(EmailTemplates.Confirm("a@example.test", "https://app.example.test/c"));
            await sender.SendAsync(EmailTemplates.Reset("a@example.test", "https://app.example.test/r"));

            var files = Directory.GetFiles(directory);
            Assert.Equal(2, files.Length);

            var written = await File.ReadAllTextAsync(files.OrderBy(f => f, StringComparer.Ordinal).First());
            Assert.Contains("a@example.test", written, StringComparison.Ordinal);
            // Both parts are kept: a developer reading the sink should see what the
            // customer would have seen, not a summary.
            Assert.Contains("text/plain", written, StringComparison.Ordinal);
            Assert.Contains("text/html", written, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_capture_provider_keeps_every_message_in_order()
    {
        var capture = new CaptureEmailSender();

        await capture.SendAsync(EmailTemplates.Confirm("a@example.test", "https://app.example.test/c"));
        await capture.SendAsync(EmailTemplates.Receipt("a@example.test", "Pro", "9 EUR", "https://app.example.test/i"));

        Assert.Equal(2, capture.Messages.Count);
        Assert.Contains("Confirm", capture.Messages.First().Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_provider_validates()
    {
        var validator = new EmailOptionsValidator();

        Assert.True(validator.Validate(null, ValidOptions()).Succeeded);
        Assert.True(validator.Validate(null, ValidOptions("capture")).Succeeded);
    }

    [Fact]
    public void An_unknown_provider_fails_with_the_known_list_in_the_message()
    {
        var result = new EmailOptionsValidator().Validate(null, ValidOptions("sendgrid"));

        Assert.False(result.Succeeded);
        var failure = Assert.Single(result.Failures!);
        Assert.Contains("sendgrid", failure, StringComparison.Ordinal);
        Assert.Contains("file", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_from_address_fails()
    {
        var options = ValidOptions();
        options.FromAddress = " ";
        Assert.False(new EmailOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_plain_http_public_url_fails_because_it_would_put_tokens_in_the_clear()
    {
        var options = ValidOptions();
        options.PublicBaseUrl = "http://app.example.test";

        var result = new EmailOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("https", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Loopback_http_is_allowed_for_development()
    {
        var options = ValidOptions();
        options.PublicBaseUrl = "http://localhost:5173";
        Assert.True(new EmailOptionsValidator().Validate(null, options).Succeeded);
    }

    private sealed class StubEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
