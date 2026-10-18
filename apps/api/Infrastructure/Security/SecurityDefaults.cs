namespace SwarmApp.Api.Infrastructure.Security;

/// <summary>
/// Baseline response headers for every response.
/// </summary>
/// <remarks>
/// The template shipped none of these, which is the kind of gap nobody notices until
/// a browser refuses to do something, or a scanner reports it. Two are worth
/// explaining because they are usually applied wrong:
///
///  - **CSP** allows <c>'self'</c> scripts only. Vite emits external bundles, so there
///    is no need for <c>'unsafe-inline'</c> on scripts — and allowing it is the same as
///    not having a CSP. Styles keep <c>'unsafe-inline'</c> because React sets inline
///    style attributes, which is a materially smaller risk than inline script.
///  - **HSTS** is emitted only outside Development. Sending it from a local http
///    origin pins <c>localhost</c> to https in the developer's browser and is
///    genuinely painful to undo.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    /// <summary>The CSP this template ships. Adjust per product, never by deleting it.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        if (!environment.IsDevelopment())
        {
            headers["Strict-Transport-Security"] = StrictTransportSecurity;
        }

        await next(context);
    }
}

/// <summary>
/// A deliberately small, redacted view of the running configuration.
/// </summary>
/// <remarks>
/// "Is this deployment configured the way I think it is?" is the first question during
/// an incident, and the honest answer used to be "read the environment". This answers
/// it without ever returning a value that could be a credential.
///
/// The rule is an **allow-list of reportable keys**, not a deny-list of secret-looking
/// ones: a deny-list is wrong the first time somebody adds a secret with an unusual
/// name. Any key that is not explicitly reportable does not appear at all.
/// </remarks>
public static class ConfigSnapshot
{
    /// <summary>Keys whose values are safe to show verbatim.</summary>
    private static readonly (string Key, string Label)[] Reportable =
    [
        ("Email:Provider", "emailProvider"),
        ("Email:FromAddress", "emailFrom"),
        ("Database:MigrateOnStartup", "migrateOnStartup"),
        ("OpenTelemetry:Endpoint", "telemetryEndpoint"),
        ("ASPNETCORE_ENVIRONMENT", "environment"),
    ];

    /// <summary>Keys reported only as present/absent.</summary>
    private static readonly (string Key, string Label)[] PresenceOnly =
    [
        ("ConnectionStrings:Default", "databaseConfigured"),
        ("Email:PublicBaseUrl", "emailPublicBaseUrlConfigured"),
    ];

    public static IReadOnlyDictionary<string, string?> Describe(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var snapshot = new SortedDictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, label) in Reportable)
        {
            snapshot[label] = string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key];
        }

        foreach (var (key, label) in PresenceOnly)
        {
            snapshot[label] = string.IsNullOrWhiteSpace(configuration[key]) ? "no" : "yes";
        }

        return snapshot;
    }
}
