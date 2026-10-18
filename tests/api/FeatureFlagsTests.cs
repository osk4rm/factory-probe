using Microsoft.Extensions.Configuration;
using SwarmApp.Api.Infrastructure;

namespace SwarmApp.Api.Tests;

/// <summary>
/// T-14. The whole value of a feature flag is that its failure mode is "off", so that is what
/// these assert: unknown, missing, malformed and empty all mean off.
/// </summary>
public sealed class FeatureFlagsTests
{
    private static FeatureFlags Flags(params (string Key, string? Value)[] pairs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs
                .Select(pair => new KeyValuePair<string, string?>($"Features:{pair.Key}", pair.Value)))
            .Build();

        return new FeatureFlags(configuration);
    }

    [Fact]
    public void A_configured_true_flag_is_enabled()
    {
        Assert.True(Flags(("NewCheckout", "true")).IsEnabled("NewCheckout"));
    }

    [Fact]
    public void A_configured_false_flag_is_disabled()
    {
        Assert.False(Flags(("NewCheckout", "false")).IsEnabled("NewCheckout"));
    }

    [Fact]
    public void An_unknown_flag_is_off_rather_than_an_error()
    {
        // A typo in a flag name must disable a feature, never enable it and never throw.
        Assert.False(Flags(("NewCheckout", "true")).IsEnabled("NewCheckoutTypo"));
    }

    [Fact]
    public void A_malformed_value_is_off()
    {
        // "yes", "1" and "" are the shapes an operator will actually type.
        Assert.False(Flags(("A", "yes")).IsEnabled("A"));
        Assert.False(Flags(("A", "1")).IsEnabled("A"));
        Assert.False(Flags(("A", "")).IsEnabled("A"));
        Assert.False(Flags(("A", null)).IsEnabled("A"));
    }

    [Fact]
    public void An_empty_name_is_off()
    {
        Assert.False(Flags(("A", "true")).IsEnabled("  "));
    }

    [Fact]
    public void Flags_are_case_insensitive_because_configuration_keys_are()
    {
        Assert.True(Flags(("NewCheckout", "true")).IsEnabled("newcheckout"));
    }

    [Fact]
    public void All_reports_what_is_configured_and_nothing_more()
    {
        var flags = Flags(("On", "true"), ("Off", "false"), ("Bogus", "maybe"));

        var all = flags.All();

        Assert.Equal(3, all.Count);
        Assert.True(all["On"]);
        Assert.False(all["Off"]);
        // A value that is not a boolean is off rather than an exception, so a diagnostics
        // endpoint can report it instead of failing.
        Assert.False(all["Bogus"]);
    }

    [Fact]
    public void An_unconfigured_section_reports_no_flags()
    {
        Assert.Empty(new FeatureFlags(new ConfigurationBuilder().Build()).All());
    }
}
