namespace SwarmApp.Api.Infrastructure.Billing;

/// <summary>The URL the client should be sent to. Opaque to us by design.</summary>
public sealed record BillingSession(string Url);

/// <summary>
/// Creates hosted payment sessions.
/// </summary>
/// <remarks>
/// An interface because the provider is a network call, and because the template must build
/// and run with billing unconfigured. It is deliberately **not** abstracted further: card
/// data never touches this application, since the customer completes payment on the
/// provider's page. That is what keeps the template out of PCI scope, and it is why this
/// interface has two methods rather than a subscription manager.
/// </remarks>
public interface IBillingSessionProvider
{
    Task<BillingSession> CreateCheckoutAsync(Guid userId, string email, CancellationToken cancellationToken = default);

    Task<BillingSession> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The provider used when billing is not configured.
/// </summary>
/// <remarks>
/// Throws rather than returning a placeholder: a checkout endpoint that answers with a
/// plausible-looking URL when no payment provider exists is the kind of bug that is found by
/// a customer rather than by a test.
/// </remarks>
public sealed class UnconfiguredBillingSessionProvider : IBillingSessionProvider
{
    public Task<BillingSession> CreateCheckoutAsync(Guid userId, string email, CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException();

    public Task<BillingSession> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException();
}

public sealed class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException()
        : base("billing is not configured; set Billing:SecretKey and Billing:PriceId") { }
}
