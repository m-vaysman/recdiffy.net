using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Writes a 32nds price rounded to the field's scale with the profile midpoint, so the key sees the value the diff compares (#170).
internal sealed class Scaled32ndsRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "price.from32nds";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = Treasury32nds.Apply(row, from, to, scale, midpoint, RuleId);
}
