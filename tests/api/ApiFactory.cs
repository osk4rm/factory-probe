using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace SwarmApp.Api.Tests;

/// <summary>
/// Boots the API against a throwaway database, applying migrations from the real EF Core
/// model so tests exercise the production schema.
///
/// **Two substrates, one fixture.** By default it starts a PostgreSQL container, which is
/// the right thing on a machine with a working Docker daemon. When
/// <c>FACTORY_TEST_DB</c> is set it uses that connection string instead — which is what
/// lets the suite run on a host where Docker is unavailable, using a PostgreSQL started as
/// an ordinary user process. The alternative was no integration tests at all on that host,
/// and an untested auth flow is worse than a slower one.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer? _database;

    public ApiFactory()
    {
        var external = Environment.GetEnvironmentVariable("FACTORY_TEST_DB");
        string connectionString;

        if (!string.IsNullOrWhiteSpace(external))
        {
            connectionString = external;
        }
        else
        {
            _database = new PostgreSqlBuilder("postgres:17-alpine").Build();
            _database.StartAsync().GetAwaiter().GetResult();
            connectionString = _database.GetConnectionString();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
        // The capture provider keeps every message in memory, so a test can read what the
        // application would have sent without a network or a mail account.
        Environment.SetEnvironmentVariable("Email__Provider", "capture");
        Environment.SetEnvironmentVariable("Email__PublicBaseUrl", "https://app.test");
        Environment.SetEnvironmentVariable("Billing__WebhookSecret", "whsec_integration_secret");
        Environment.SetEnvironmentVariable("Analytics__Sink", "recording");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _database is not null)
        {
            _database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

/// <summary>Persists Set-Cookie values so cookie auth works across requests.</summary>
internal sealed class CookieAppender(CookieContainer container) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var cookieHeader = container.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                container.SetCookies(request.RequestUri!, value);
            }
        }

        return response;
    }
}
