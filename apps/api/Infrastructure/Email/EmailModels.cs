using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace SwarmApp.Api.Infrastructure.Email;

/// <summary>One transactional message. Deliberately provider-neutral.</summary>
public sealed record EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    /// <summary>HTML body. Every template in this project ships both bodies.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Plain-text body. Not optional: a message with only HTML scores as spam with
    /// several providers, and every template here is written once and rendered twice.
    /// </summary>
    public required string TextBody { get; init; }

    public string? ReplyTo { get; init; }
}

/// <summary>
/// Provider selection and its options.
/// </summary>
/// <remarks>
/// The default is <c>file</c>, which needs no infrastructure at all. That is what
/// lets a fresh clone run the whole signup flow — and its tests — without a mail
/// account, an API key or a network. <c>smtp</c> and API providers arrive when a
/// sending domain exists; the interface is what this template commits to, not a vendor.
/// </remarks>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>file</c> (default) or <c>capture</c> (in-memory, for tests).</summary>
    public string Provider { get; set; } = "file";

    public string FromAddress { get; set; } = "no-reply@localhost";

    public string? FromName { get; set; }

    /// <summary>
    /// Absolute base URL used to build confirmation and reset links. Required once a
    /// link-bearing template is sent, because a relative link in an email is a link
    /// nobody can click.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public FileSinkOptions File { get; set; } = new();
}

public sealed class FileSinkOptions
{
    /// <summary>Directory the file sink writes one file per message into.</summary>
    public string Directory { get; set; } = "data/emails";
}

/// <summary>
/// Validates the email configuration at startup rather than at first send.
/// </summary>
/// <remarks>
/// A misconfigured provider that only fails when a customer clicks "reset password"
/// is worse than a service that refuses to boot: the first is discovered by a
/// customer, the second by the deploy.
/// </remarks>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public static readonly string[] KnownProviders = ["file", "capture"];

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();

        if (!KnownProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Email:Provider is '{options.Provider}', which is not a known provider " +
                $"(known: {string.Join(", ", KnownProviders)}).");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) || !options.FromAddress.Contains('@', StringComparison.Ordinal))
        {
            failures.Add("Email:FromAddress must be an email address.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var uri))
            {
                failures.Add("Email:PublicBaseUrl must be an absolute URL when set.");
            }
            else if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            {
                // A confirmation link over plain http is a token in the clear.
                failures.Add("Email:PublicBaseUrl must use https (loopback is allowed for development).");
            }
        }

        if (string.Equals(options.Provider, "file", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.File.Directory))
        {
            failures.Add("Email:File:Directory must be set when the file provider is used.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// The three emails a product needs before it can take a customer, rendered once and
/// used by every provider.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Confirm(string to, string confirmUrl) =>
        Build(
            to,
            "Confirm your email address",
            "Confirm your email address",
            $"Confirm your email address to finish creating your account. The link expires in 24 hours.",
            confirmUrl,
            "Confirm email",
            "If you did not create an account, ignore this message — nothing was activated.");

    public static EmailMessage Reset(string to, string resetUrl) =>
        Build(
            to,
            "Reset your password",
            "Reset your password",
            "Use the link below to choose a new password. The link expires in 2 hours and works once.",
            resetUrl,
            "Choose a new password",
            "If you did not ask for a password reset, ignore this message — your password has not changed.");

    public static EmailMessage Receipt(string to, string planName, string amount, string invoiceUrl) =>
        Build(
            to,
            $"Your receipt for {planName}",
            "Payment received",
            $"Thanks — your payment for {planName} ({amount}) was received. The invoice is available with the link below.",
            invoiceUrl,
            "Open the invoice",
            "You can cancel at any time from your account settings; access continues until the end of the paid period.");

    /// <summary>
    /// Renders both bodies from one paragraph and one call to action, so they cannot
    /// drift. Every interpolated value is HTML-escaped: a product name is user
    /// input in at least one of these messages.
    /// </summary>
    private static EmailMessage Build(
        string to,
        string subject,
        string heading,
        string paragraph,
        string url,
        string actionLabel,
        string footer)
    {
        if (!IsHttpUrl(url))
        {
            throw new ArgumentException($"email link must be absolute, got '{url}'", nameof(url));
        }

        var html = new StringBuilder()
            .Append("<!doctype html><html><body style=\"font-family:system-ui,sans-serif\">")
            .Append("<h1 style=\"font-size:20px\">").Append(WebUtility.HtmlEncode(heading)).Append("</h1>")
            .Append("<p>").Append(WebUtility.HtmlEncode(paragraph)).Append("</p>")
            .Append("<p><a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\" ")
            .Append("style=\"display:inline-block;padding:10px 16px;background:#111;color:#fff;border-radius:6px;text-decoration:none\">")
            .Append(WebUtility.HtmlEncode(actionLabel)).Append("</a></p>")
            .Append("<p style=\"color:#555;font-size:13px\">").Append(WebUtility.HtmlEncode(footer)).Append("</p>")
            .Append("</body></html>")
            .ToString();

        var text = new StringBuilder()
            .Append(heading).Append('\n').Append('\n')
            .Append(paragraph).Append('\n').Append('\n')
            .Append(actionLabel).Append(": ").Append(url).Append('\n').Append('\n')
            .Append(footer).Append('\n')
            .ToString();

        return new EmailMessage { To = to, Subject = subject, HtmlBody = html, TextBody = text };
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
