using System.Net;
using System.Net.Http.Json;
using SwarmApp.Api.Contracts;

namespace SwarmApp.Api.Tests;

public sealed class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient CreateClient()
    {
        var cookies = new CookieContainer();
        return factory.CreateDefaultClient(new CookieAppender(cookies));
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        return csrf!.Token;
    }

    [Fact]
    public async Task Health_live_endpoint_responds()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Register_then_me_returns_the_new_user()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(client));

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "user@example.com",
            password = "Sup3rSecret!",
            displayName = "Test User",
        });
        register.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Equal("user@example.com", me!.Email);
        Assert.Contains("user", me.Roles);
    }

    [Fact]
    public async Task Me_without_a_session_returns_unauthorized()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CsrfResponse(string Token);
}
