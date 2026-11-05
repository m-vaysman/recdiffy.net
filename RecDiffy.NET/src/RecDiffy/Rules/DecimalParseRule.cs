namespace RecDiffy.Rules;

internal sealed class DecimalParseRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly int _scale;
    private readonly MidpointMode _midpoint;

    public DecimalParseRule(string from, string to, int scale, MidpointMode midpoint)
    {
        _from = from;
        _to = to;
        _scale = scale;
        _midpoint = midpoint;
    }

    public string RuleId => "decimal.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row) =>
        row = DecimalParsing.Apply(row, _from, _to, _scale, _midpoint, RuleId);
}
