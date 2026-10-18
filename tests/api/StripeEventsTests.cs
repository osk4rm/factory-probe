using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Tests;

/// <summary>
/// Mapping provider events onto our own state. Pure functions over JSON: no database, no
/// network, no Stripe account.
/// </summary>
public sealed class StripeEventsTests
{
    private const string CheckoutCompleted = """
    {"id":"evt_1","type":"checkout.session.completed","data":{"object":{"id":"cs_1","customer":"cus_1","subscription":"sub_1"}}}
    """;

    private const string SubscriptionUpdated = """
    {"id":"evt_2","type":"customer.subscription.updated","data":{"object":{"id":"sub_1","customer":"cus_1","status":"active","current_period_end":1800000000}}}
    """;

    [Fact]
    public void Checkout_completion_activates_the_referenced_subscription()
    {
        var result = StripeEvents.Parse(CheckoutCompleted);

        Assert.Equal("evt_1", result.Id);
        Assert.Equal("checkout.session.completed", result.Type);
        Assert.NotNull(result.Change);
        // The session object references the subscription; it does not embed it.
        Assert.Equal("sub_1", result.Change!.SubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, result.Change.Status);
    }

    [Fact]
    public void The_event_id_is_carried_through_because_it_is_the_idempotency_key()
    {
        // Stripe retries deliveries, so the same event arriving twice must be applied once.
        Assert.Equal("evt_2", StripeEvents.Parse(SubscriptionUpdated).Id);
    }

    [Fact]
    public void A_subscription_update_carries_status_and_period_end()
    {
        var change = StripeEvents.Parse(SubscriptionUpdated).Change;

        Assert.Equal(SubscriptionStatus.Active, change!.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), change.CurrentPeriodEnd);
    }

    [Fact]
    public void Past_due_is_its_own_state_rather_than_silently_active()
    {
        var payload = """{"id":"e","type":"invoice.payment_failed","data":{"object":{"subscription":"sub_1","customer":"cus_1"}}}""";

        Assert.Equal(SubscriptionStatus.PastDue, StripeEvents.Parse(payload).Change!.Status);
    }

    [Fact]
    public void Deletion_cancels_rather_than_setting_a_flag()
    {
        // A cancel_at_period_end flag means it will end later; deleted means it has ended.
        var payload = """{"id":"e","type":"customer.subscription.deleted","data":{"object":{"id":"sub_1","status":"canceled","current_period_end":1800000000}}}""";

        var change = StripeEvents.Parse(payload).Change;

        Assert.Equal(SubscriptionStatus.Cancelled, change!.Status);
    }

    [Fact]
    public void An_unknown_event_type_is_not_an_error()
    {
        // Stripe adds types. A webhook that 500s on an unrecognised event is retried forever.
        var payload = """{"id":"e","type":"invoice.upcoming","data":{"object":{"id":"in_1"}}}""";

        var result = StripeEvents.Parse(payload);

        Assert.Equal("invoice.upcoming", result.Type);
        Assert.Null(result.Change);
    }

    [Fact]
    public void An_event_without_an_id_or_type_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => StripeEvents.Parse("""{"type":"x"}"""));
        Assert.Throws<InvalidOperationException>(() => StripeEvents.Parse("""{"id":"e"}"""));
    }

    [Fact]
    public void A_subscription_status_we_do_not_understand_is_reported_as_unknown()
    {
        var payload = """{"id":"e","type":"customer.subscription.updated","data":{"object":{"id":"sub_1","status":"paused_forever"}}}""";

        Assert.Equal(SubscriptionStatus.Unknown, StripeEvents.Parse(payload).Change!.Status);
    }
}
