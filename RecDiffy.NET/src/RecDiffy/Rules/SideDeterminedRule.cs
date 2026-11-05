namespace RecDiffy.Rules;

internal sealed class SideDeterminedFactory : Engine.IRuleFactory
{
    public string RuleId => "side.determined";
    public RulePhase Phase => RulePhase.Validate;
    public INormalizationRule Bind(RuleArguments args) => new SideDeterminedRule();
}

internal sealed class SideDeterminedRule : INormalizationRule
{
    public string RuleId => "side.determined";
    public RulePhase Phase => RulePhase.Validate;
    public IReadOnlyList<FieldId> Reads { get; } = [new FieldId("Side")];
    public IReadOnlyList<FieldId> Writes { get; } = [];

    public void Apply(ref RowContext row)
    {
        if (!row.TryCanonical("Side", out _))
            row.Diagnose("UndeterminedSide", DiagnosticSeverity.Invalid, "Side");
    }
}
