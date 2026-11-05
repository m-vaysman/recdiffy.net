namespace RecDiffy.Rules;

internal sealed class TextCopyRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;

    public TextCopyRule(string from, string to)
    {
        _from = from;
        _to = to;
    }

    public string RuleId => "text.copy";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row) =>
        row = TextCopying.Apply(row, _from, _to, RuleId);
}
