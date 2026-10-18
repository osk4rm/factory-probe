using System.Text.Json;

namespace SwarmApp.Api.Infrastructure.Billing;

/// <summary>
/// The subscription state machine, driven by provider events.
/// </summary>
/// <remarks>
/// Deliberately provider-shaped rather than provider-neutral in one place only: Stripe's
/// event names are the source of truth for *what happened*, and translating them here keeps
/// that translation in one file instead of scattered through a controller.
///
/// Two rules that matter more than the mapping itself:
///
///  - **An unknown event type is not an error.** Stripe adds types; a webhook endpoint that
///    500s on an unrecognised event makes Stripe retry it forever. Unknown returns null.
///  - **The event id is the idempotency key**, and it is carried through. Stripe retries
///    deliveries, so the same event will arrive more than once and must be applied once.
/// </remarks>
public sealed record BillingEvent(
    string Id,
    string Type,
    BillingChange? Change);

public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
    Unknown,
}

/// <summary>What to apply to our own records. Null when the event does not change them.</summary>
public sealed record BillingChange(
    string SubscriptionId,
    string? CustomerId,
    SubscriptionStatus Status,
    DateTimeOffset? CurrentPeriodEnd);

public static class StripeEvents
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static BillingEvent Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;

        if (id.Length == 0 || type.Length == 0)
        {
            throw new InvalidOperationException("a webhook event must carry an id and a type");
        }

        return new BillingEvent(id, type, Map(root, type));
    }

    private static BillingChange? Map(JsonElement root, string type)
    {
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("object", out var obj))
        {
            return null;
        }

        var subscriptionId = String(obj, "subscription") ?? String(obj, "id");
        var customerId = String(obj, "customer");

        return type switch
        {
            // Checkout completed: the subscription object is referenced, not embedded.
            "checkout.session.completed" => subscriptionId is null
                ? null
                : new BillingChange(subscriptionId, customerId, SubscriptionStatus.Active, null),

            "customer.subscription.created" or "customer.subscription.updated" =>
                subscriptionId is null
                    ? null
                    : new BillingChange(subscriptionId, customerId, StatusOf(String(obj, "status")), PeriodEnd(obj)),

            // Deleted means the period has genuinely ended. A cancellation flag on the object
            // only means it will end later, which is why this is a separate event.
            "customer.subscription.deleted" => subscriptionId is null
                ? null
                : new BillingChange(subscriptionId, customerId, SubscriptionStatus.Cancelled, PeriodEnd(obj)),

            // A failed payment must not silently keep access: past-due is its own state, and
            // the product decides how long to tolerate it.
            "invoice.payment_failed" => subscriptionId is null
                ? null
                : new BillingChange(subscriptionId, customerId, SubscriptionStatus.PastDue, null),

            _ => null,
        };
    }

    private static SubscriptionStatus StatusOf(string? status) => status switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Cancelled,
        _ => SubscriptionStatus.Unknown,
    };

    private static DateTimeOffset? PeriodEnd(JsonElement obj)
    {
        if (!obj.TryGetProperty("current_period_end", out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        return element.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
