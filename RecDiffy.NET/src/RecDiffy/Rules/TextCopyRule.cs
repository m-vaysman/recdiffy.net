namespace RecDiffy.Rules;

internal sealed class TextCopyRule(string from, string to) : INormalizationRule
{
    public string RuleId => "text.copy";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = TextCopying.Apply(row, from, to, RuleId);
}
