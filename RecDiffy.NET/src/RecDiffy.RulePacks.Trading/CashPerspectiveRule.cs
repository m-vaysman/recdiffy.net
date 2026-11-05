using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class CashPerspectiveFactory : IRuleFactory
{
    public string RuleId => "cash.toPerspective";
    public RulePhase Phase => RulePhase.Derive;
    public INormalizationRule Bind(RuleArguments args) => new CashPerspectiveRule(args.Optional("field") ?? "NetCash", args.Optional("perspective") ?? "Reporting_Perspective", args.Optional("whenValue") ?? "DEALER_BILATERAL_VIEW");
}

internal sealed class CashPerspectiveRule : INormalizationRule
{
    private readonly string _field;
    private readonly string _perspective;
    private readonly string _when;
    public CashPerspectiveRule(string field, string perspective, string when)
    {
        _field = field;
        _perspective = perspective;
        _when = when;
    }

    public string RuleId => "cash.toPerspective";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<FieldId> Reads => [new(_field)];
    public IReadOnlyList<FieldId> Writes => [new(_field)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_perspective, out var perspective) || perspective.Text != _when)
            return;
        if (!row.TryCanonical(_field, out var cash))
            return;
        row.SetDecimal(_field, -cash.Number, cash.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
