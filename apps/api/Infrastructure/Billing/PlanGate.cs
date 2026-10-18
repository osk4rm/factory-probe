using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace SwarmApp.Api.Infrastructure.Billing;

/// <summary>
/// Where "is this user paying?" comes from.
/// </summary>
/// <remarks>
/// An interface rather than a direct table read, for two reasons that both matter here:
/// the gate must be testable without a database, and the source of truth for a subscription
/// is the payment provider, not our own row — our row is a cache of it. When the two
/// disagree, the provider wins and the row is wrong.
/// </remarks>
public interface ISubscriptionStatusLookup
{
    Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class ActiveSubscriptionRequirement : IAuthorizationRequirement;

/// <summary>
/// Enforces a paid plan **server-side**.
/// </summary>
/// <remarks>
/// A gate that only hides a button is not a gate: the endpoint is still reachable with curl.
/// This is the check that runs regardless of what the client renders, and it is deliberately
/// a policy rather than a filter inside a controller, so it cannot be forgotten on a new
/// endpoint that happens to live in the same class.
///
/// Three outcomes, and the middle one is the one people get wrong:
///
///  - no identity claim  → fail, because an anonymous caller has no plan;
///  - identity, unpaid   → fail;
///  - identity, paid     → success.
///
/// A `PastDue` subscription is **not** active here. Whether a product tolerates a grace
/// period is a product decision, and it belongs in the lookup implementation — not silently
/// in the gate, where it would apply to every product that shares this template.
/// </remarks>
public sealed class ActiveSubscriptionHandler(ISubscriptionStatusLookup lookup)
    : AuthorizationHandler<ActiveSubscriptionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveSubscriptionRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId)) return;

        if (await lookup.HasActiveSubscriptionAsync(userId))
        {
            context.Succeed(requirement);
        }
    }
}

public static class PlanGate
{
    /// <summary>The policy name a paid endpoint names in `[Authorize(Policy = …)]`.</summary>
    public const string Policy = "ActiveSubscription";

    public static IServiceCollection AddPlanGate(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveSubscriptionRequirement()));
        });

        services.AddScoped<IAuthorizationHandler, ActiveSubscriptionHandler>();
        return services;
    }
}

/// <summary>
/// Treats every user as unpaid. The default, so a product that has not configured billing
/// cannot accidentally be free-for-everyone: the gate fails closed.
/// </summary>
public sealed class NoSubscriptionsLookup : ISubscriptionStatusLookup
{
    public Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
