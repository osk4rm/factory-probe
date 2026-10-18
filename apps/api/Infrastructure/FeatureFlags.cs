namespace SwarmApp.Api.Infrastructure;

/// <summary>
/// Configuration-driven feature flags.
/// </summary>
/// <remarks>
/// Deliberately the smallest thing that works: a flag is a boolean in configuration, read
/// through one method. There is no database, no admin UI and no targeting - those are product
/// features, and a template that ships them ships the bugs in them.
///
/// Two rules that make it safe to use:
///
///  - **An unknown flag is off.** Not an exception, not a warning: off. A typo in a flag name
///    must disable a feature, never enable it, and never take down a request.
///  - **The default is off**, so a flag that is missing from configuration behaves exactly
///    like one explicitly set to false. Enabling something is always an affirmative act.
/// </remarks>
public sealed class FeatureFlags(IConfiguration configuration)
{
    public const string SectionName = "Features";

    /// <summary>True only when configured true. Unknown and unset are both false.</summary>
    public bool IsEnabled(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var value = configuration[$"{SectionName}:{name}"];
        return bool.TryParse(value, out var enabled) && enabled;
    }

    /// <summary>
    /// Every configured flag, for a diagnostics endpoint.
    /// </summary>
    /// <remarks>
    /// Read from the section rather than from a static registry so the two cannot disagree;
    /// the names are returned as configured, which is what an operator will match against.
    /// </remarks>
    public IReadOnlyDictionary<string, bool> All()
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in configuration.GetSection(SectionName).GetChildren())
        {
            flags[child.Key] = bool.TryParse(child.Value, out var enabled) && enabled;
        }

        return flags;
    }
}

public static class FeatureFlagRegistration
{
    public static IServiceCollection AddSwarmFeatureFlags(this IServiceCollection services) =>
        services.AddSingleton<FeatureFlags>();
}
