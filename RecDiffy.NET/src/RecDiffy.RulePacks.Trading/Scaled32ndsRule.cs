using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Writes a 32nds price rounded to the field's scale with the profile midpoint, so the key sees the value the diff compares (#170).
// Replaced by BlankAware32ndsRule (#274). An empty cell reaches it as "", which Treasury32nds.Apply rejects as PriceParse.
// Friend tests still construct it; deleting a replaced rule is left to a human.
#pragma warning disable MA0182
[Obsolete("Replaced by BlankAware32ndsRule: treats an empty cell as PriceParse instead of blank. See #274.")]
internal sealed class Scaled32ndsRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
#pragma warning restore MA0182
{
    public string RuleId => "price.from32nds";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = Treasury32nds.Apply(row, from, to, scale, midpoint, RuleId);
}
