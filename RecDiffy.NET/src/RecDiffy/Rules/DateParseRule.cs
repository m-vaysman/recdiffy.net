namespace RecDiffy.Rules;

internal sealed class DateParseRule(string from, string to, IReadOnlyList<string> formats) : INormalizationRule
{
    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = DateParsing.Apply(row, from, to, formats, RuleId);
}
