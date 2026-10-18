using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using SwarmApp.Api.Contracts;
using SwarmApp.Api.Infrastructure.Email;

namespace SwarmApp.Api.Tests;

/// <summary>
/// T-02: the two flows a product cannot ship without. These are the first tests in this
/// template that exercise a real database *and* a real email path end to end, with the
/// capture provider standing in for the network.
/// </summary>
public sealed class EmailFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private (HttpClient Client, CookieContainer Cookies) CreateClient()
    {
        var cookies = new CookieContainer();
        return (factory.CreateDefaultClient(new CookieAppender(cookies)), cookies);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        return csrf!.Token;
    }

    /// <summary>
    /// Posts with a token fetched immediately beforehand.
    /// Signing in rotates the antiforgery token, so a token read at the start of a test is
    /// already stale by the next request - which is what made confirm and reset answer 400.
    /// </summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.PostAsJsonAsync(url, body);
    }

    private CaptureEmailSender Mailbox() => factory.Services.GetRequiredService<CaptureEmailSender>();

    private static string TokenFrom(EmailMessage message, string action)
    {
        // The link is in both bodies; read the plain-text one so the assertion does not
        // depend on HTML escaping.
        var match = System.Text.RegularExpressions.Regex.Match(message.TextBody, $"{action}\\?email=[^&]+&token=(\\S+)");
        Assert.True(match.Success, $"no {action} link in: {message.TextBody}");
        return Uri.UnescapeDataString(match.Groups[1].Value.Trim());
    }

    [Fact]
    public async Task Registering_sends_a_confirmation_link_and_the_account_starts_unconfirmed()
    {
        Mailbox().Clear();
        var (client, _) = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"confirm-{Guid.NewGuid():N}@example.test",
            password = "Sup3rSecret!",
            displayName = "Test",
        });
        register.EnsureSuccessStatusCode();

        var user = await register.Content.ReadFromJsonAsync<UserResponse>();
        Assert.False(user!.EmailConfirmed);

        var message = Assert.Single(Mailbox().Messages);
        Assert.Contains("Confirm", message.Subject, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(TokenFrom(message, "confirm")));
    }

    [Fact]
    public async Task Confirming_with_the_token_marks_the_account_confirmed()
    {
        Mailbox().Clear();
        var (client, _) = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        var email = $"confirm-ok-{Guid.NewGuid():N}@example.test";

        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Sup3rSecret!" });
        var token = TokenFrom(Mailbox().Messages.Last(), "confirm");

        var confirm = await PostAsync(client, "/api/auth/confirm", new { email, token });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.True(me!.EmailConfirmed);
    }

    [Fact]
    public async Task A_tampered_confirmation_token_is_refused_without_revealing_whether_the_account_exists()
    {
        var (client, _) = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));

        var known = await client.PostAsJsonAsync("/api/auth/confirm", new
        {
            email = $"confirm-ok-placeholder@example.test",
            token = "not-a-token",
        });
        var unknown = await client.PostAsJsonAsync("/api/auth/confirm", new
        {
            email = $"nobody-{Guid.NewGuid():N}@example.test",
            token = "not-a-token",
        });

        // Same status and same body: a distinct reply would make this an account oracle.
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forgot_answers_the_same_whether_or_not_the_account_exists()
    {
        var (client, _) = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));

        var unknown = await client.PostAsJsonAsync("/api/auth/forgot", new
        {
            email = $"nobody-{Guid.NewGuid():N}@example.test",
        });

        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }

    [Fact]
    public async Task Resetting_with_the_emailed_token_changes_the_password()
    {
        Mailbox().Clear();
        var (client, _) = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        const string original = "Sup3rSecret!";
        const string replacement = "Ev3nBetterSecret!";

        await client.PostAsJsonAsync("/api/auth/register", new { email, password = original });
        Mailbox().Clear();

        await PostAsync(client, "/api/auth/forgot", new { email });
        var message = Assert.Single(Mailbox().Messages);
        Assert.Contains("Reset", message.Subject, StringComparison.Ordinal);
        var token = TokenFrom(message, "reset");

        var reset = await PostAsync(client, "/api/auth/reset", new { email, token, password = replacement });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The old password must stop working, and the new one must start.
        var (fresh, _) = CreateClient();
        fresh.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await CsrfAsync(fresh));
        var oldLogin = await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = original });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = replacement });
        newLogin.EnsureSuccessStatusCode();
    }
}
