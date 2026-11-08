namespace RecDiffy.Rules;

// Replaced by InvariantSymbolMapRule (#158). It turns a raw number into text with the server's culture.
[Obsolete("Replaced by InvariantSymbolMapRule: turns a raw number into text with the server's culture. See #158.")]
internal sealed class SymbolMapRule(
    string from,
    string to,
    IReadOnlyDictionary<string, string> map,
    bool passthrough) : INormalizationRule
{
    public string RuleId => "symbol.map";

    public RulePhase Phase => RulePhase.Identify;

    // The source column is raw text, so the read set stays empty.
    // Listing it makes compile report FieldReadBeforeWrite.
    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = SymbolMapping.Apply(row, from, to, map, passthrough, RuleId);
}
