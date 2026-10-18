using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwarmApp.Api.Infrastructure.Billing;

/// <summary>
/// Verifies a Stripe webhook signature without taking a dependency on the Stripe SDK.
/// </summary>
/// <remarks>
/// This is the whole security model of a payment webhook: the endpoint is public, and the
/// only thing separating a real subscription change from anyone's POST is this HMAC. It is
/// written out rather than delegated because the rules that are easy to get wrong are also
/// the ones that matter:
///
///  - **The signed payload is `timestamp + "." + body`**, not the body alone. Signing the
///    body alone lets an attacker replay a captured request forever.
///  - **The comparison is constant-time.** A byte-by-byte `==` leaks how much of a guessed
///    signature was correct, one request at a time.
///  - **Stale timestamps are refused.** A signed request is still valid forever otherwise,
///    so a captured webhook can be replayed after a refund to re-grant access.
///  - **Several `v1` signatures may be present** during a secret rotation, and any one
///    matching is a success — otherwise rotating the secret drops live events.
/// </remarks>
public static class StripeSignature
{
    /// <summary>
    /// Default replay window. Five minutes is Stripe's own default and is long enough for a
    /// retried delivery and short enough that a replay is impractical.
    /// </summary>
    public const int DefaultToleranceSeconds = 300;

    public sealed record Result(bool Valid, string? Reason)
    {
        public static Result Ok() => new(true, null);
        public static Result Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// Validates the `Stripe-Signature` header against the raw request body.
    /// </summary>
    /// <param name="payload">The exact bytes received. Re-serialised JSON will not match.</param>
    /// <param name="header">The `Stripe-Signature` header value.</param>
    /// <param name="secret">The endpoint's signing secret.</param>
    /// <param name="now">Injected so the replay window is testable.</param>
    public static Result Verify(
        string payload,
        string? header,
        string? secret,
        int toleranceSeconds = DefaultToleranceSeconds,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret)) return Result.Fail("no signing secret configured");
        if (string.IsNullOrWhiteSpace(header)) return Result.Fail("missing signature header");

        long? timestamp = null;
        var candidates = new List<string>();

        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var name = part[..separator];
            var value = part[(separator + 1)..];

            if (name == "t" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                timestamp = parsed;
            }
            else if (name == "v1")
            {
                candidates.Add(value);
            }
        }

        if (timestamp is null) return Result.Fail("signature header has no timestamp");
        if (candidates.Count == 0) return Result.Fail("signature header has no v1 signature");

        var at = now ?? DateTimeOffset.UtcNow;
        var age = Math.Abs(at.ToUnixTimeSeconds() - timestamp.Value);
        if (age > toleranceSeconds)
        {
            // Refused rather than accepted: a captured webhook must not outlive its window.
            return Result.Fail($"timestamp is {age}s outside the {toleranceSeconds}s tolerance");
        }

        var signed = $"{timestamp.Value}.{payload}";
        var expected = HmacSha256Hex(secret, signed);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        foreach (var candidate in candidates)
        {
            // FixedTimeEquals on the raw bytes: comparison time must not depend on how much
            // of the guess was right.
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), expectedBytes))
            {
                return Result.Ok();
            }
        }

        return Result.Fail("no signature matched");
    }

    /// <summary>Exposed for the tests that build a valid header; the scheme is public.</summary>
    public static string HmacSha256Hex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
