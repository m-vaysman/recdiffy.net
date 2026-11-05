using System.Globalization;
using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class QuantityAbsoluteFactory : IRuleFactory
{
    public string RuleId => "quantity.absolute";
    public RulePhase Phase => RulePhase.Derive;
    public INormalizationRule Bind(RuleArguments args) => new QuantityAbsoluteRule(args.Required("from"), args.Optional("to") ?? "Quantity");
}

internal sealed class QuantityAbsoluteRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    public QuantityAbsoluteRule(string from, string to) { _from = from; _to = to; }
    public string RuleId => "quantity.absolute";
    public RulePhase Phase => RulePhase.Derive;
    public IReadOnlyList<FieldId> Reads { get; } = [];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull)
        {
            row.SetNull(_to);
            return;
        }
        if (raw.Number is not decimal number && !decimal.TryParse(raw.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            row.Diagnose("DecimalParse", DiagnosticSeverity.Invalid, _to, raw.Text);
            return;
        }
        row.SetDecimal(_to, decimal.Abs(number), raw.Text);
    }
}
