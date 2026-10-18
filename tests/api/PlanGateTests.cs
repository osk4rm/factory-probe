using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Tests;

/// <summary>
/// The plan gate is the half of billing a UI cannot enforce: the endpoint is still reachable
/// with curl. Tested against a fake lookup, which is exactly why the lookup is an interface.
/// </summary>
public sealed class PlanGateTests
{
    private sealed class FakeLookup(bool active) : ISubscriptionStatusLookup
    {
        public Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(active);
    }

    private static ClaimsPrincipal User(Guid? id) =>
        new(new ClaimsIdentity(
            id is null ? [] : [new Claim(ClaimTypes.NameIdentifier, id.Value.ToString())],
            id is null ? null : "test"));

    private static async Task<bool> EvaluateAsync(bool paid, Guid? userId)
    {
        var handler = new ActiveSubscriptionHandler(new FakeLookup(paid));
        var context = new AuthorizationHandlerContext(
            [new ActiveSubscriptionRequirement()],
            userId is null ? new ClaimsPrincipal(new ClaimsIdentity()) : User(userId),
            resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task A_paying_user_passes() => Assert.True(await EvaluateAsync(paid: true, Guid.NewGuid()));

    [Fact]
    public async Task An_unpaid_user_is_refused() => Assert.False(await EvaluateAsync(paid: false, Guid.NewGuid()));

    [Fact]
    public async Task An_anonymous_caller_is_refused_even_if_the_lookup_says_paid()
    {
        // No identity means no plan. A lookup answering "yes" must not make an anonymous
        // request succeed.
        Assert.False(await EvaluateAsync(paid: true, userId: null));
    }

    [Fact]
    public async Task The_default_lookup_fails_closed()
    {
        // A product that has not configured billing must not be free for everyone.
        Assert.False(await new NoSubscriptionsLookup().HasActiveSubscriptionAsync(Guid.NewGuid()));
    }

    [Fact]
    public void The_policy_name_is_stable()
    {
        // Endpoints reference this string; renaming it silently ungates them.
        Assert.Equal("ActiveSubscription", PlanGate.Policy);
    }
}
