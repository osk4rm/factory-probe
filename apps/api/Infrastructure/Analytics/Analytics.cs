using System.Collections.Concurrent;

namespace SwarmApp.Api.Infrastructure.Analytics;

/// <summary>
/// An analytics event, in the shape every vendor can accept.
/// </summary>
/// <remarks>
/// **Vendor-neutral on purpose.** The template must not import a vendor SDK into its domain
/// code, because a vendor choice made in week one becomes a migration in year two. The shape
/// here — a name and a flat bag of scalars — maps onto every product analytics and webhook
/// sink that exists, so changing vendor is a new sink implementation and nothing else.
/// </remarks>
public sealed record AnalyticsEvent(string Name, IReadOnlyDictionary<string, object?> Properties);

public interface IAnalytics
{
    /// <summary>Records an event. Must never throw into a request path.</summary>
    void Track(string name, IReadOnlyDictionary<string, object?>? properties = null);
}

/// <summary>
/// The default: records nothing.
/// </summary>
/// <remarks>
/// The template ships with no analytics vendor and no outbound traffic. A no-op that is
/// always safe is better than a "TODO" interface, because the call sites are then written
/// and tested from day one and enabling analytics later is a registration change.
/// </remarks>
public sealed class NullAnalytics : IAnalytics
{
    public void Track(string name, IReadOnlyDictionary<string, object?>? properties = null)
    {
        // Deliberately empty.
    }
}

/// <summary>
/// A bounded in-memory sink: the local-development and test implementation.
/// </summary>
/// <remarks>
/// Bounded because an unbounded diagnostic buffer is a memory leak that only appears under
/// the load where you most want the application to keep working.
/// </remarks>
public sealed class RecordingAnalytics(int capacity = 500) : IAnalytics
{
    private readonly ConcurrentQueue<AnalyticsEvent> _events = new();

    public IReadOnlyList<AnalyticsEvent> Events => _events.ToArray();

    public void Track(string name, IReadOnlyDictionary<string, object?>? properties = null)
    {
        _events.Enqueue(new AnalyticsEvent(name, properties ?? new Dictionary<string, object?>()));
        while (_events.Count > capacity) _events.TryDequeue(out _);
    }

    public void Clear()
    {
        while (_events.TryDequeue(out _)) { }
    }
}

/// <summary>
/// Stops personally identifying data from reaching an analytics sink.
/// </summary>
/// <remarks>
/// This is the guard that makes "we only send aggregates" true rather than aspirational. It
/// is a deny-list of property *names*, because that is the only thing checkable at the call
/// site: an email buried in a free-text value cannot be detected without inspecting content,
/// and inspecting content is not something a request path should do.
///
/// Anything rejected is dropped, not redacted: a partially-recorded event is harder to
/// reason about than a missing one.
/// </remarks>
public static class AnalyticsGuard
{
    private static readonly string[] Forbidden = new[]
    {
        "email", "password", "passwd", "secret", "token", "authorization",
        "cookie", "apikey", "api_key", "card", "cvv", "iban", "phone", "address",
    };

    public static bool IsForbidden(string propertyName)
    {
        var normalised = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return Forbidden.Any(forbidden => normalised.Contains(forbidden, StringComparison.Ordinal));
    }

    /// <summary>Returns the properties that may be sent, and the names that were dropped.</summary>
    public static (IReadOnlyDictionary<string, object?> Safe, IReadOnlyList<string> Dropped) Sanitise(
        IReadOnlyDictionary<string, object?>? properties)
    {
        var safe = new Dictionary<string, object?>();
        var dropped = new List<string>();

        foreach (var (key, value) in properties ?? new Dictionary<string, object?>())
        {
            if (IsForbidden(key))
            {
                dropped.Add(key);
                continue;
            }
            safe[key] = value;
        }

        return (safe, dropped);
    }
}

/// <summary>
/// The implementation every call site actually uses: sanitises, then forwards.
/// </summary>
/// <remarks>
/// Sanitising in the decorator rather than in `Track` implementations means a new sink cannot
/// forget the guard, which is the failure mode that matters - a sink added by someone in a
/// hurry is exactly the place a customer email ends up.
/// </remarks>
public sealed class GuardedAnalytics(IAnalytics inner) : IAnalytics
{
    public void Track(string name, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var (safe, _) = AnalyticsGuard.Sanitise(properties);
        try
        {
            inner.Track(name, safe);
        }
        catch
        {
            // Analytics must never break a request. A failed event is not a failed signup.
        }
    }
}

public sealed class AnalyticsSettings
{
    public const string SectionName = "Analytics";

    /// <summary>`none` (default), `recording`, or a vendor name once one is implemented.</summary>
    public string Sink { get; set; } = "none";
}

public static class AnalyticsRegistration
{
    public static IServiceCollection AddSwarmAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        var sink = configuration[$"{AnalyticsSettings.SectionName}:{nameof(AnalyticsSettings.Sink)}"] ?? "none";

        if (sink.Equals("recording", StringComparison.OrdinalIgnoreCase))
        {
            // Registered separately so a diagnostic reader can inspect it; the guarded
            // decorator is what call sites receive.
            services.AddSingleton<RecordingAnalytics>();
            services.AddSingleton<IAnalytics>(provider =>
                new GuardedAnalytics(provider.GetRequiredService<RecordingAnalytics>()));
        }
        else
        {
            services.AddSingleton<IAnalytics>(new GuardedAnalytics(new NullAnalytics()));
        }

        return services;
    }
}
