namespace RecDiffy;

// Called by RecDiffy.RulePacks.Trading and RecDiffy.Tests via InternalsVisibleTo (#235).
#pragma warning disable MA0182
internal static class AttributionRegistration
#pragma warning restore MA0182
{
    public static RuleRegistry RegisterAttribution(this RuleRegistry registry, IAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry with { Attributions = registry.Attributions.Register(attribution) };
    }
}
