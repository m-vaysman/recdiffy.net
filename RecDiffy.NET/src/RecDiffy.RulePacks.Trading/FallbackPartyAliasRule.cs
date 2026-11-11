using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal sealed class FallbackPartyAliasRule(string from, string to) : INormalizationRule
{
    public string RuleId => "party.resolveAlias";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads => [new(to)];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = PartyLookup.Alias(row, from, to, RuleId);
}
