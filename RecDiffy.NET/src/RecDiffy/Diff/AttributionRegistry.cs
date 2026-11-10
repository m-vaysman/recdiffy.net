using System.Collections.Immutable;

namespace RecDiffy;

internal sealed record AttributionRegistry
{
    public static AttributionRegistry Empty { get; } = new();

    private ImmutableDictionary<string, IAttribution> Attributions { get; init; } =
        ImmutableDictionary.Create<string, IAttribution>(StringComparer.Ordinal);

    public AttributionRegistry Register(IAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        return this with { Attributions = Attributions.Add(attribution.Name, attribution) };
    }

    public IAttribution? Find(string? name) =>
        name is not null && Attributions.TryGetValue(name, out var attribution) ? attribution : null;
}
