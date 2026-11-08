namespace RecDiffy.Rules;

// A raw number becomes invariant text, so the copy reads the same on every server (#158).
internal sealed class InvariantTextCopyRule(string from, string to) : INormalizationRule
{
    public string RuleId => "text.copy";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? InvariantText.Of(value)?.Trim() : null;
        row = row.Write(to, CanonicalCell.FromText(raw), RuleId, raw, raw);
    }
}
