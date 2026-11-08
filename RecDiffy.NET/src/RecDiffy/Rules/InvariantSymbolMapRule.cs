namespace RecDiffy.Rules;

// A raw number becomes invariant text before it is mapped, so a map key reads the same on every server (#158).
internal sealed class InvariantSymbolMapRule(string from, string to, IReadOnlyDictionary<string, string> map, bool passthrough) : INormalizationRule
{
    public string RuleId => "symbol.map";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) => row = Map(row);

    private RowContext Map(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? InvariantText.Of(value)?.Trim() : null;
        return raw switch
        {
            null or "" => row.Write(to, CanonicalCell.Null(FieldType.Symbol), RuleId, raw, null),
            var text when map.TryGetValue(text, out var mapped) => Intern(row, mapped, text),
            var text when passthrough => Intern(row, text, text),
            var text => row.Write(to, CanonicalCell.Null(FieldType.Symbol), RuleId, text, null)
                .Diagnose("UnknownSymbol", DiagnosticSeverity.Invalid, RuleId, to, text),
        };
    }

    private RowContext Intern(RowContext row, string symbol, string raw)
    {
        var (pool, id) = row.Pool.InternSymbol(symbol);
        return row.WithPool(pool).Write(to, CanonicalCell.FromInterned(FieldType.Symbol, id), RuleId, raw, symbol);
    }
}
