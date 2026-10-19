using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Data;

/// <summary>
/// Our cache of a payment provider's subscription state.
/// </summary>
/// <remarks>
/// **A cache, not the source of truth.** The provider owns the subscription; this row exists
/// so a request can answer "is this user paying?" without a network call. When the two
/// disagree, the provider wins and this row is repaired by the next webhook — which is why
/// every webhook carries the full current state rather than a delta.
///
/// The provider's identifiers are stored rather than ours: `ProviderSubscriptionId` is
/// unique, so a redelivered webhook updates the same row instead of creating a second one.
/// </remarks>
public sealed class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>Non-null by construction: a subscription we cannot correlate is not stored.</summary>
    public required string ProviderSubscriptionId { get; set; }

    public string? ProviderCustomerId { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unknown;

    /// <summary>End of the paid period, as the provider reported it.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
