namespace RecDiffy.Rules;

// Replaced by ParseDecimalRule (found in #150). decimal.parse is registered to the new rule; nothing creates this one.
[Obsolete("Replaced by ParseDecimalRule: reads a decimal comma as a thousands separator, so \"1,5\" is 15. See #150.")]
internal sealed class DecimalParseRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "decimal.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = DecimalParsing.Apply(row, from, to, scale, midpoint, RuleId);
}
