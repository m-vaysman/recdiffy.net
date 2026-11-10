namespace RecDiffy;

internal static class AttributionRegistration
{
    public static RuleRegistry RegisterAttribution(this RuleRegistry registry, IAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry with { Attributions = registry.Attributions.Register(attribution) };
    }
}
