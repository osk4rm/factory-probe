using System.Net;
using System.Net.Http.Json;
using SwarmApp.Api.Contracts;

namespace SwarmApp.Api.Tests;

/// <summary>
/// Checkout and portal with no provider configured.
/// </summary>
/// <remarks>
/// These assert the failure paths on purpose. Real session creation needs Stripe test-mode
/// credentials and is **not** covered here — a test against a fake provider would only show
/// that a fake was called, which is the kind of green that hides an unbuilt feature.
/// </remarks>
public sealed class BillingSessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Registers and signs in. The CSRF header must be present on `register` itself, and
    /// signing in then rotates the token, so it is replaced afterwards.
    /// </summary>
    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.CreateDefaultClient(new CookieAppender(new CookieContainer()));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"pay-{Guid.NewGuid():N}@example.test",
            password = "Sup3rSecret!",
        });
        register.EnsureSuccessStatusCode();

        // Signing in rotates the antiforgery token; replace rather than reuse the stale one.
        var fresh = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", fresh!.Token);
        return client;
    }

    [Fact]
    public async Task Checkout_requires_authentication()
    {
        var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/billing/checkout", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_fails_closed_when_no_provider_is_configured()
    {
        var client = await SignedInAsync();

        var response = await client.PostAsJsonAsync("/api/billing/checkout", new { });

        // 503, never a plausible URL: with no payment provider there is nothing to buy.
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync());
        Assert.Contains("billing_not_configured", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portal_reports_no_subscription_for_a_user_without_one()
    {
        var client = await SignedInAsync();

        var response = await client.PostAsJsonAsync("/api/billing/portal", new { });

        Assert.True(response.StatusCode == HttpStatusCode.NotFound, (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync());
    }
}
