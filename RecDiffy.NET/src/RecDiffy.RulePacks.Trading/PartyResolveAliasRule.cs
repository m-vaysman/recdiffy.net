using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class PartyResolveAliasFactory : IRuleFactory
{
    public string RuleId => "party.resolveAlias";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new PartyResolveAliasRule(args.Required("from"), args.Required("to"));
}

internal sealed class PartyResolveAliasRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    public PartyResolveAliasRule(string from, string to) { _from = from; _to = to; }
    public string RuleId => "party.resolveAlias";
    public RulePhase Phase => RulePhase.Identify;
    public IReadOnlyList<FieldId> Reads { get; } = [new("Counterparty")];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (row.TryCanonical(_to, out _))
            return;
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
            return;
        var alias = raw.Text.Trim();
        if (!row.Reference.AliasToCode.TryGetValue(alias, out var code))
        {
            row.Diagnose("UnknownParty", DiagnosticSeverity.Invalid, _to, alias);
            return;
        }
        row.SetIdentifier(_to, "party", "", code, alias);
    }
}
