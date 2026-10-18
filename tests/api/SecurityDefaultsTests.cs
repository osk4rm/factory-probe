using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SwarmApp.Api.Infrastructure.Security;

namespace SwarmApp.Api.Tests;

/// <summary>
/// Unit tests for the response headers and the redacted configuration view.
/// Deliberately no <see cref="ApiFactory"/>: neither concern needs a database or a
/// network, and a security test that only runs where Docker exists is one that does
/// not run where it is most useful.
/// </summary>
public sealed class SecurityDefaultsTests
{
    private static async Task<IHeaderDictionary> RunAsync(bool development)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var environment = new StubEnvironment(
            development ? Environments.Development : Environments.Production);

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, environment);
        await middleware.InvokeAsync(context);

        return context.Response.Headers;
    }

    [Fact]
    public async Task Every_response_carries_the_baseline_headers()
    {
        var headers = await RunAsync(development: true);

        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        // The policy names these features in order to disable them: `camera=()` means
        // "no origin may use it", which is the opposite of permitting it.
        Assert.Contains("camera=()", headers["Permissions-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("microphone=()", headers["Permissions-Policy"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_content_security_policy_forbids_inline_script()
    {
        var headers = await RunAsync(development: false);
        var csp = headers["Content-Security-Policy"].ToString();

        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'self'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hsts_is_sent_in_production_and_withheld_in_development()
    {
        var production = await RunAsync(development: false);
        Assert.Equal("max-age=31536000; includeSubDomains", production["Strict-Transport-Security"].ToString());

        // Sending HSTS from a local http origin pins localhost to https in the
        // developer's browser, which is genuinely annoying to undo.
        var development = await RunAsync(development: true);
        Assert.Equal(string.Empty, development["Strict-Transport-Security"].ToString());
    }

    [Fact]
    public void The_config_snapshot_reports_providers_and_flags_but_never_a_credential()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "file",
                ["Email:FromAddress"] = "no-reply@example.test",
                ["Email:PublicBaseUrl"] = "https://app.example.test",
                ["Database:MigrateOnStartup"] = "true",
                ["ConnectionStrings:Default"] = "Host=db;Password=hunter2",
                ["GitHub:AppPrivateKey"] = "-----BEGIN RSA PRIVATE KEY-----",
                ["Stripe:SecretKey"] = "sk_live_should_never_appear",
            })
            .Build();

        var snapshot = ConfigSnapshot.Describe(configuration);

        Assert.Equal("file", snapshot["emailProvider"]);
        Assert.Equal("no-reply@example.test", snapshot["emailFrom"]);
        Assert.Equal("true", snapshot["migrateOnStartup"]);
        // Presence, not value: a connection string is a credential.
        Assert.Equal("yes", snapshot["databaseConfigured"]);

        var serialized = string.Join(';', snapshot.Select(entry => $"{entry.Key}={entry.Value}"));
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live", serialized, StringComparison.Ordinal);
        // An unknown key is absent, not passed through: the rule is an allow-list.
        Assert.DoesNotContain(snapshot.Keys, key => key.Contains("GitHub", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Keys, key => key.Contains("Stripe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_values_are_reported_as_absent_rather_than_as_a_blank_string()
    {
        var snapshot = ConfigSnapshot.Describe(new ConfigurationBuilder().Build());

        Assert.Null(snapshot["emailProvider"]);
        Assert.Equal("no", snapshot["databaseConfigured"]);
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
