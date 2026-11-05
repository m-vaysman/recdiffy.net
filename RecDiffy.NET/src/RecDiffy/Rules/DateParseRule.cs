namespace RecDiffy.Rules;

internal sealed class DateParseRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly IReadOnlyList<string> _formats;

    public DateParseRule(string from, string to, IReadOnlyList<string> formats)
    {
        _from = from;
        _to = to;
        _formats = formats;
    }

    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row) =>
        row = DateParsing.Apply(row, _from, _to, _formats, RuleId);
}
