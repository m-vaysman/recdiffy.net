using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class DecimalParseFactory : Engine.IRuleFactory
{
    public string RuleId => "decimal.parse";
    public RulePhase Phase => RulePhase.Parse;
    public INormalizationRule Bind(RuleArguments args) => new DecimalParseRule(args.Required("from"), args.Required("to"), args.Optional("scale") is string scale ? int.Parse(scale, CultureInfo.InvariantCulture) : -1);
}

internal sealed class DecimalParseRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly int _scaleOverride;
    public DecimalParseRule(string from, string to, int scaleOverride)
    {
        _from = from;
        _to = to;
        _scaleOverride = scaleOverride;
    }

    public string RuleId => "decimal.parse";
    public RulePhase Phase => RulePhase.Parse;
    public IReadOnlyList<FieldId> Reads { get; } = [];
    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull)
        {
            row.SetNull(_to);
            return;
        }
        if (raw.Number is not decimal number)
        {
            if (raw.Text is null || !decimal.TryParse(raw.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            {
                row.Diagnose("DecimalParse", DiagnosticSeverity.Invalid, _to, raw.Text);
                return;
            }
        }
        row.SetDecimal(_to, number, raw.Text ?? number.ToString(CultureInfo.InvariantCulture));
    }
}
