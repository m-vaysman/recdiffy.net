namespace RecDiffy.Rules;

// A raw number becomes invariant text, so a numeric key reads the same on every server and on both sides (#158).
internal sealed class InvariantIdentifierMapRule(string from, string to, string scheme, string scope) : INormalizationRule
{
    public string RuleId => "identifier.map";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) => row = Map(row);

    private RowContext Map(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? InvariantText.Of(value)?.Trim() : null;
        return string.IsNullOrEmpty(raw)
            ? row.Write(to, CanonicalCell.Null(FieldType.Identifier), RuleId, raw, null)
            : Intern(row, raw);
    }

    private RowContext Intern(RowContext row, string value)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, scope, value);
        return row.WithPool(pool).Write(to, CanonicalCell.FromInterned(FieldType.Identifier, id), RuleId, value, value);
    }
}
