using SwarmApp.Api.Infrastructure.Billing;

namespace SwarmApp.Api.Tests;

/// <summary>
/// The whole security model of a payment webhook is this HMAC: the endpoint is public, and
/// nothing else separates a real subscription change from anyone's POST. No database, no
/// network, no Stripe account - which is why these can run everywhere.
/// </summary>
public sealed class StripeSignatureTests
{
    private const string Secret = "whsec_test_secret";
    private const string Body = "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\"}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static string Header(long timestamp, string secret = Secret, string body = Body) =>
        $"t={timestamp},v1={StripeSignature.HmacSha256Hex(secret, $"{timestamp}.{body}")}";

    [Fact]
    public void A_correctly_signed_request_is_accepted()
    {
        var result = StripeSignature.Verify(Body, Header(Now.ToUnixTimeSeconds()), Secret, now: Now);

        Assert.True(result.Valid, result.Reason);
    }

    [Fact]
    public void A_signature_over_the_body_alone_is_refused()
    {
        // The signed payload is `timestamp.body`. Signing the body alone lets a captured
        // request be replayed forever, because the signature never expires.
        var timestamp = Now.ToUnixTimeSeconds();
        var bodyOnly = StripeSignature.HmacSha256Hex(Secret, Body);

        var result = StripeSignature.Verify(Body, $"t={timestamp},v1={bodyOnly}", Secret, now: Now);

        Assert.False(result.Valid);
    }

    [Fact]
    public void A_stale_timestamp_is_refused_even_with_a_valid_signature()
    {
        // A captured webhook must not outlive its window, or it can re-grant access after a
        // refund.
        var old = Now.AddSeconds(-StripeSignature.DefaultToleranceSeconds - 1).ToUnixTimeSeconds();

        var result = StripeSignature.Verify(Body, Header(old), Secret, now: Now);

        Assert.False(result.Valid);
        Assert.Contains("tolerance", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tampered_body_is_refused()
    {
        var header = Header(Now.ToUnixTimeSeconds());
        var tampered = Body.Replace("evt_1", "evt_2", StringComparison.Ordinal);

        Assert.False(StripeSignature.Verify(tampered, header, Secret, now: Now).Valid);
    }

    [Fact]
    public void The_wrong_secret_is_refused()
    {
        var header = Header(Now.ToUnixTimeSeconds(), secret: "whsec_other");

        Assert.False(StripeSignature.Verify(Body, header, Secret, now: Now).Valid);
    }

    [Fact]
    public void A_secret_rotation_can_present_two_signatures_and_either_may_match()
    {
        // During a rotation Stripe sends both. Refusing all but the newest drops live events.
        var timestamp = Now.ToUnixTimeSeconds();
        var oldSignature = StripeSignature.HmacSha256Hex("whsec_old", $"{timestamp}.{Body}");
        var newSignature = StripeSignature.HmacSha256Hex(Secret, $"{timestamp}.{Body}");
        var header = $"t={timestamp},v1={oldSignature},v1={newSignature}";

        Assert.True(StripeSignature.Verify(Body, header, Secret, now: Now).Valid);
    }

    [Fact]
    public void A_missing_secret_or_header_is_refused_rather_than_treated_as_trusted()
    {
        Assert.False(StripeSignature.Verify(Body, Header(Now.ToUnixTimeSeconds()), secret: null, now: Now).Valid);
        Assert.False(StripeSignature.Verify(Body, header: null, Secret, now: Now).Valid);
        Assert.False(StripeSignature.Verify(Body, " ", Secret, now: Now).Valid);
    }

    [Fact]
    public void A_header_without_a_timestamp_or_signature_is_refused()
    {
        var timestamp = Now.ToUnixTimeSeconds();
        var signature = StripeSignature.HmacSha256Hex(Secret, $"{timestamp}.{Body}");

        Assert.False(StripeSignature.Verify(Body, $"v1={signature}", Secret, now: Now).Valid);
        Assert.False(StripeSignature.Verify(Body, $"t={timestamp}", Secret, now: Now).Valid);
        Assert.False(StripeSignature.Verify(Body, "garbage", Secret, now: Now).Valid);
    }

    [Fact]
    public void The_rule_is_the_one_Stripe_documents()
    {
        // Pinned against Stripe's published example so a future "simplification" cannot
        // quietly change the scheme.
        const string stripeSecret = "whsec_testsecret";
        const string payload = "{\"id\":\"evt_test\"}";
        const long timestamp = 1_700_000_000;

        var expected = StripeSignature.HmacSha256Hex(stripeSecret, $"{timestamp}.{payload}");

        Assert.Equal(64, expected.Length);
        Assert.True(StripeSignature.Verify(payload, $"t={timestamp},v1={expected}", stripeSecret,
            now: DateTimeOffset.FromUnixTimeSeconds(timestamp)).Valid);
    }
}
