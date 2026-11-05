using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class PartyResolveLeiFactory : IRuleFactory
{
    public string RuleId => "party.resolveLei";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new PartyResolveLeiRule(args.Required("from"), args.Required("to"));
}

internal sealed class PartyResolveLeiRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    public PartyResolveLeiRule(string from, string to) { _from = from; _to = to; }
    public string RuleId => "party.resolveLei";
    public RulePhase Phase => RulePhase.Identify;
    public IReadOnlyList<FieldId> Reads { get; } = [];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (row.TryCanonical(_to, out _))
            return;
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
            return;
        var lei = raw.Text.Trim();
        if (!row.Reference.LeiToCode.TryGetValue(lei, out var code))
        {
            row.Diagnose("UnknownParty", DiagnosticSeverity.Invalid, _to, lei);
            return;
        }
        row.SetIdentifier(_to, "party", "", code, lei);
    }
}
