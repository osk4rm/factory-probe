using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SwarmApp.Api.Infrastructure.Analytics;

namespace SwarmApp.Api.Tests;

/// <summary>
/// T-13: a vendor-neutral analytics interface whose guard actually stops personal data.
/// </summary>
/// <remarks>
/// The interesting tests are the guard ones. "We only send aggregates" is easy to claim and
/// easy to break, so the deny-list is asserted directly, and the decorator is asserted to be
/// what call sites receive - a new sink must not be able to bypass it.
/// </remarks>
public sealed class AnalyticsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static IReadOnlyDictionary<string, object?> Props(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void The_guard_drops_anything_that_looks_like_personal_data()
    {
        var (safe, dropped) = AnalyticsGuard.Sanitise(Props(
            ("method", "password"),
            ("email", "a@example.test"),
            ("userEmail", "a@example.test"),
            ("card_number", "4242"),
            ("access_token", "abc"),
            ("count", 3)));

        Assert.Equal(["access_token", "card_number", "email", "userEmail"], dropped.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["count", "method"], safe.Keys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("Email")]
    [InlineData("user_email")]
    [InlineData("userEmail")]
    [InlineData("phone-number")]
    [InlineData("apiKey")]
    public void Casing_and_separators_do_not_defeat_the_guard(string name)
    {
        Assert.True(AnalyticsGuard.IsForbidden(name));
    }

    [Theory]
    [InlineData("method")]
    [InlineData("count")]
    [InlineData("provider")]
    [InlineData("duration_ms")]
    public void Ordinary_properties_are_allowed(string name)
    {
        Assert.False(AnalyticsGuard.IsForbidden(name));
    }

    [Fact]
    public void The_default_sink_records_nothing_and_never_throws()
    {
        var analytics = new NullAnalytics();
        analytics.Track("anything", Props(("email", "a@example.test")));
        analytics.Track("nothing");
    }

    [Fact]
    public void A_failing_sink_does_not_break_the_request()
    {
        // Analytics must never fail a signup. This is the whole reason for the decorator.
        var guarded = new GuardedAnalytics(new ThrowingSink());
        guarded.Track("user.registered", Props(("method", "password")));
    }

    [Fact]
    public void Call_sites_receive_the_guarded_decorator_rather_than_a_raw_sink()
    {
        using var scope = factory.Services.CreateScope();
        Assert.IsType<GuardedAnalytics>(scope.ServiceProvider.GetRequiredService<IAnalytics>());
    }

    [Fact]
    public async Task Registering_emits_an_event_with_no_personal_data()
    {
        var recorder = factory.Services.GetRequiredService<RecordingAnalytics>();
        recorder.Clear();

        // The cookie appender is required: the CSRF cookie must survive to the POST.
        var client = factory.CreateDefaultClient(new CookieAppender(new System.Net.CookieContainer()));
        var csrf = await client.GetFromJsonAsync<Contracts.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        var email = $"analytics-{Guid.NewGuid():N}@example.test";
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Sup3rSecret!" }))
            .EnsureSuccessStatusCode();

        var registered = Assert.Single(recorder.Events, e => e.Name == "user.registered");

        // The event carries a method, not an identity.
        Assert.Equal("password", registered.Properties["method"]);
        Assert.DoesNotContain(registered.Properties.Values, value => value is string text && text.Contains(email, StringComparison.Ordinal));
        Assert.DoesNotContain(registered.Properties.Keys, AnalyticsGuard.IsForbidden);
    }

    private sealed class ThrowingSink : IAnalytics
    {
        public void Track(string name, IReadOnlyDictionary<string, object?>? properties = null) =>
            throw new InvalidOperationException("sink is down");
    }
}
