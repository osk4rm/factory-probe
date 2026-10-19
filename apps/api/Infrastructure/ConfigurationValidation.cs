using Microsoft.Extensions.Options;

namespace SwarmApp.Api.Infrastructure;

/// <summary>
/// Startup validation for the two configuration sections that fail silently when wrong.
/// </summary>
/// <remarks>
/// Email already validates itself. These are the other two places where a bad value does
/// not throw — it merely behaves worse:
///
///  - **CORS.** A wildcard origin with credentials is rejected by ASP.NET at first use,
///    which is an error in production rather than at deploy. Worse, a bare `*` without
///    credentials compiles, runs, and quietly lets any site call the API.
///  - **Telemetry.** A malformed OTLP endpoint does not fail; the exporter retries into a
///    void and the deployment looks fine while emitting nothing.
///
/// Both are cheap to check and expensive to discover later, which is the whole argument for
/// `ValidateOnStart`.
/// </remarks>
public sealed class CorsOptionsValidator : IValidateOptions<CorsSettings>
{
    public ValidateOptionsResult Validate(string? name, CorsSettings options)
    {
        var failures = new List<string>();

        foreach (var origin in options.AllowedOrigins)
        {
            if (origin.Trim() == "*")
            {
                failures.Add(
                    "Cors:AllowedOrigins contains '*'. The API sends credentials, and a wildcard " +
                    "origin with credentials is refused by the browser and by ASP.NET. Name the origins.");
                continue;
            }

            // Explicitly require http or https with a host, rather than relying on what
            // `Uri.TryCreate` decides a "relative" path is. On Windows `/app` fails to parse as
            // absolute; on Linux it can parse as a file URI - so a validator written against
            // the negative case passed on one platform and failed on the other, which is
            // exactly what CI found when this repository first ran on a Linux runner.
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrEmpty(uri.Host))
            {
                failures.Add(
                    $"Cors:AllowedOrigins entry '{origin}' is not an absolute http(s) URL with a host. " +
                    "A browser will refuse to honour anything else.");
            }
            else if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                failures.Add($"Cors:AllowedOrigins entry '{origin}' must use https (loopback is allowed for development).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class TelemetryOptionsValidator : IValidateOptions<TelemetrySettings>
{
    public ValidateOptionsResult Validate(string? name, TelemetrySettings options)
    {
        // Unset is valid: an OTLP endpoint is optional, and a developer without a collector
        // should not have to configure a fake one.
        if (string.IsNullOrWhiteSpace(options.Endpoint)) return ValidateOptionsResult.Success;

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri))
        {
            return ValidateOptionsResult.Fail(
                $"OpenTelemetry:Endpoint '{options.Endpoint}' is not an absolute URL; " +
                "the exporter would retry into a void and the deployment would look healthy.");
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            return ValidateOptionsResult.Fail(
                $"OpenTelemetry:Endpoint '{options.Endpoint}' must be http or https, got '{uri.Scheme}'.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class TelemetrySettings
{
    public const string SectionName = "OpenTelemetry";

    public string? Endpoint { get; set; }
}

public static class ConfigurationValidation
{
    /// <summary>Wires both validators and makes them run at startup rather than at first use.</summary>
    public static IServiceCollection AddSwarmConfigurationValidation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CorsSettings>()
            .Bind(configuration.GetSection(CorsSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsSettings>, CorsOptionsValidator>();

        services.AddOptions<TelemetrySettings>()
            .Bind(configuration.GetSection(TelemetrySettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelemetrySettings>, TelemetryOptionsValidator>();

        return services;
    }
}
