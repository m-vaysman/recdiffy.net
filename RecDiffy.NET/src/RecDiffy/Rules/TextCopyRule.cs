namespace RecDiffy.Rules;

// Replaced by InvariantTextCopyRule (#158). It turns a raw number into text with the server's culture.
[Obsolete("Replaced by InvariantTextCopyRule: turns a raw number into text with the server's culture. See #158.")]
internal sealed class TextCopyRule(string from, string to) : INormalizationRule
{
    public string RuleId => "text.copy";

    public RulePhase Phase => RulePhase.Identify;

    // The source column is raw text, so the read set stays empty.
    // Listing it makes compile report FieldReadBeforeWrite.
    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = TextCopying.Apply(row, from, to, RuleId);
}
