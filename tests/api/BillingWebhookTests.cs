using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SwarmApp.Api.Data;
using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Tests;

/// <summary>
/// The webhook is public and trusted only by signature, so these tests are about trust and
/// idempotency rather than about JSON mapping. A fake provider is not needed: the signature
/// is real, computed with the same documented scheme Stripe uses.
/// </summary>
public sealed class BillingWebhookTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Secret = "whsec_integration_secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private HttpClient Client() => factory.CreateDefaultClient();

    private static HttpRequestMessage Signed(string payload, string? secret = Secret, long? at = null)
    {
        var timestamp = (at ?? Now.ToUnixTimeSeconds());
        var signature = StripeSignature.HmacSha256Hex(secret!, $"{timestamp}.{payload}");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
        return request;
    }

    private async Task<Guid> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = id, UserName = $"pay-{id:N}@example.test", Email = $"pay-{id:N}@example.test" });
        await db.SaveChangesAsync();
        return id;
    }

    private static string CheckoutPayload(Guid userId, string subscriptionId, string eventId) =>
        "{\"id\":\"" + eventId + "\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{" +
        "\"id\":\"cs_1\",\"customer\":\"cus_1\",\"subscription\":\"" + subscriptionId + "\"," +
        "\"client_reference_id\":\"" + userId + "\"}}}";

    [Fact]
    public async Task A_signed_event_creates_the_subscription_row()
    {
        var userId = await CreateUserAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";

        var response = await Client().SendAsync(Signed(CheckoutPayload(userId, subscriptionId, "evt_1")));
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Subscriptions.SingleAsync(s => s.ProviderSubscriptionId == subscriptionId);
        Assert.Equal(userId, row.UserId);
        Assert.Equal(SubscriptionStatus.Active, row.Status);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_leaves_one_row()
    {
        var userId = await CreateUserAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        var payload = CheckoutPayload(userId, subscriptionId, "evt_dup");

        // Stripe retries deliveries; both must be acknowledged.
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Signed(payload))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(Signed(payload))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.ProviderSubscriptionId == subscriptionId));
    }

    [Fact]
    public async Task An_unsigned_or_tampered_event_is_refused_and_stores_nothing()
    {
        var userId = await CreateUserAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        var payload = CheckoutPayload(userId, subscriptionId, "evt_bad");

        var tampered = payload.Replace("evt_bad", "evt_other", StringComparison.Ordinal);
        // Sign the *original* and send the tampered body: signing the tampered body would
        // produce a valid signature over the altered bytes and prove nothing.
        var timestamp = Now.ToUnixTimeSeconds();
        var signature = SwarmApp.Api.Infrastructure.Billing.StripeSignature.HmacSha256Hex(Secret, timestamp + "." + payload);
        var tamperedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(tampered, Encoding.UTF8, "application/json"),
        };
        tamperedRequest.Headers.Add("Stripe-Signature", "t=" + timestamp + ",v1=" + signature);
        var response = await Client().SendAsync(tamperedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unsigned = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().SendAsync(unsigned)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Subscriptions.CountAsync(s => s.ProviderSubscriptionId == subscriptionId));
    }

    [Fact]
    public async Task A_stale_event_is_refused_even_when_correctly_signed()
    {
        var userId = await CreateUserAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        var old = Now.AddHours(-2).ToUnixTimeSeconds();

        var response = await Client().SendAsync(Signed(CheckoutPayload(userId, subscriptionId, "evt_old"), at: old));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_cancellation_updates_the_row_rather_than_adding_one()
    {
        var userId = await CreateUserAsync();
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        var client = Client();

        (await client.SendAsync(Signed(CheckoutPayload(userId, subscriptionId, "evt_a")))).EnsureSuccessStatusCode();

        var cancellation =
            "{\"id\":\"evt_b\",\"type\":\"customer.subscription.deleted\",\"data\":{\"object\":{" +
            "\"id\":\"" + subscriptionId + "\",\"customer\":\"cus_1\",\"status\":\"canceled\"}}}";
        (await client.SendAsync(Signed(cancellation))).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Subscriptions.Where(s => s.ProviderSubscriptionId == subscriptionId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(SubscriptionStatus.Cancelled, rows[0].Status);
    }

    [Fact]
    public async Task An_uncorrelated_subscription_is_acknowledged_but_not_stored()
    {
        var subscriptionId = $"sub_{Guid.NewGuid():N}";
        var payload = """{"id":"evt_x","type":"customer.subscription.updated","data":{"object":{"id":"SUBSCRIPTION","status":"active"}}}"""
            .Replace("SUBSCRIPTION", subscriptionId, StringComparison.Ordinal);

        var response = await Client().SendAsync(Signed(payload));

        // Acknowledged so the provider stops retrying, but storing a subscription with no
        // owner would be a row nobody can explain.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Subscriptions.CountAsync(s => s.ProviderSubscriptionId == subscriptionId));
    }
}
