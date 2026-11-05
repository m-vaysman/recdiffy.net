namespace RecDiffy.Rules;

internal sealed class DecimalParseRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "decimal.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = DecimalParsing.Apply(row, from, to, scale, midpoint, RuleId);
}
