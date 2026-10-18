using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SwarmApp.Api.Data;
using Microsoft.Extensions.Options;
using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Controllers;

/// <summary>
/// The payment provider's callback. Public by necessity, trusted only by signature.
/// </summary>
[ApiController]
[Route("api/billing")]
public sealed class BillingController(
    AppDbContext db,
    IOptions<BillingSettings> settings,
    SubscriptionWriter writer,
    IBillingSessionProvider sessions,
    ILogger<BillingController> logger) : ControllerBase
{
    /// <summary>
    /// Creates a hosted checkout session and returns the URL to send the customer to.
    /// </summary>
    /// <remarks>
    /// The client only ever receives a redirect target. Nothing about the price, the
    /// interval or the trial is decided here — the provider's price id is the single source
    /// of truth, so a customer cannot buy a cheaper plan by editing a request.
    /// </remarks>
    [HttpPost("checkout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        try
        {
            var session = await sessions.CreateCheckoutAsync(userId, email);
            return Ok(new { url = session.Url });
        }
        catch (BillingNotConfiguredException)
        {
            // Fail closed and say why. A plausible-looking URL when no provider exists is
            // discovered by a customer, not by a test.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "billing_not_configured" });
        }
    }

    /// <summary>Opens the provider's hosted portal so the customer manages their own plan.</summary>
    /// <remarks>
    /// Deliberately read-only for us: cancellation, card changes and invoices are the
    /// provider's job, and reimplementing them is how a template acquires a billing bug.
    /// </remarks>
    [HttpPost("portal")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Portal()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var customerId = await db.Subscriptions
            .Where(s => s.UserId == userId && s.ProviderCustomerId != null)
            .Select(s => s.ProviderCustomerId)
            .FirstOrDefaultAsync();

        if (customerId is null) return NotFound(new { error = "no_subscription" });

        try
        {
            var session = await sessions.CreatePortalAsync(customerId);
            return Ok(new { url = session.Url });
        }
        catch (BillingNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "billing_not_configured" });
        }
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        var secret = settings.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Not configured is not the same as "accept everything": refuse loudly.
            logger.LogError("billing webhook received but Billing:WebhookSecret is not set");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // The raw bytes, exactly as received. Re-serialising the JSON changes whitespace and
        // the signature stops matching, which is the classic way this fails in production.
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        var verification = StripeSignature.Verify(payload, Request.Headers["Stripe-Signature"], secret);
        if (!verification.Valid)
        {
            logger.LogWarning("rejected a webhook: {Reason}", verification.Reason);
            return BadRequest(new { error = "invalid_signature" });
        }

        var billingEvent = StripeEvents.Parse(payload);
        if (billingEvent.Change is null)
        {
            // Acknowledge unknown types: a 4xx makes the provider retry forever.
            return Ok(new { received = true, applied = false });
        }

        await writer.ApplyAsync(billingEvent.Change, UserIdFrom(payload));
        return Ok(new { received = true, applied = true });
    }

    /// <summary>
    /// Correlates a first-time subscription with a local user via checkout's
    /// `client_reference_id`, which the checkout session sets to our user id.
    /// </summary>
    private static Guid? UserIdFrom(string payload)
    {
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("object", out var obj)) return null;

        foreach (var name in new[] { "client_reference_id", "metadata" })
        {
            if (!obj.TryGetProperty(name, out var element)) continue;
            if (name == "metadata")
            {
                if (element.TryGetProperty("userId", out var meta) &&
                    Guid.TryParse(meta.GetString(), out var fromMetadata)) return fromMetadata;
                continue;
            }
            if (Guid.TryParse(element.GetString(), out var id)) return id;
        }

        return null;
    }
}
