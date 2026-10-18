using SwarmApp.Api.Infrastructure;

namespace SwarmApp.Api.Tests;

/// <summary>
/// The two configuration sections that fail silently rather than loudly. No database, no
/// network: these are pure functions over options objects, which is exactly why they can
/// run everywhere.
/// </summary>
public sealed class ConfigurationValidationTests
{
    private static CorsSettings Cors(params string[] origins) => new() { AllowedOrigins = origins };

    [Fact]
    public void A_concrete_https_origin_is_valid()
    {
        Assert.True(new CorsOptionsValidator().Validate(null, Cors("https://app.example.test")).Succeeded);
    }

    [Fact]
    public void A_wildcard_origin_is_refused_with_the_reason()
    {
        // The API sends credentials. '*' with credentials is refused by the browser and by
        // ASP.NET, and a bare '*' without them silently lets any site call the API.
        var result = new CorsOptionsValidator().Validate(null, Cors("*"));

        Assert.False(result.Succeeded);
        Assert.Contains("credentials", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_http_origin_is_refused_but_loopback_is_allowed()
    {
        var validator = new CorsOptionsValidator();

        Assert.False(validator.Validate(null, Cors("http://app.example.test")).Succeeded);
        Assert.True(validator.Validate(null, Cors("http://localhost:5173")).Succeeded);
    }

    [Fact]
    public void A_relative_origin_is_refused()
    {
        Assert.False(new CorsOptionsValidator().Validate(null, Cors("/app")).Succeeded);
    }

    [Fact]
    public void The_default_configuration_validates()
    {
        // appsettings ships [ "http://localhost:5173" ], and a template that refuses to boot
        // on its own defaults is worse than one with no validation.
        Assert.True(new CorsOptionsValidator().Validate(null, Cors("http://localhost:5173")).Succeeded);
    }

    [Fact]
    public void An_unset_telemetry_endpoint_is_valid()
    {
        // A developer without a collector should not need a fake endpoint.
        Assert.True(new TelemetryOptionsValidator().Validate(null, new TelemetrySettings()).Succeeded);
        Assert.True(new TelemetryOptionsValidator().Validate(null, new TelemetrySettings { Endpoint = "  " }).Succeeded);
    }

    [Fact]
    public void A_malformed_telemetry_endpoint_is_refused()
    {
        // Not "otel-collector:4317": Uri.TryCreate accepts that as a URI whose scheme is
        // "otel-collector", which is a different (and equally real) failure - see below.
        var result = new TelemetryOptionsValidator().Validate(null, new TelemetrySettings { Endpoint = "not a url" });

        Assert.False(result.Succeeded);
        // The failure explains the consequence, not just the rule.
        Assert.Contains("retry", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_http_telemetry_scheme_is_refused()
    {
        var validator = new TelemetryOptionsValidator();

        Assert.False(validator.Validate(null, new TelemetrySettings { Endpoint = "ftp://collector/1" }).Succeeded);
        // `host:port` parses as a URI with scheme "host", which is the mistake an operator
        // actually makes: the collector address from compose, pasted without http://.
        var bare = validator.Validate(null, new TelemetrySettings { Endpoint = "otel-collector:4317" });
        Assert.False(bare.Succeeded);
        Assert.Contains("http", Assert.Single(bare.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_telemetry_default_is_valid()
    {
        Assert.True(
            new TelemetryOptionsValidator().Validate(null, new TelemetrySettings { Endpoint = "http://localhost:4317" }).Succeeded);
    }
}
