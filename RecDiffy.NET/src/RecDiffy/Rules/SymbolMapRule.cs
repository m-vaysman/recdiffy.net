namespace RecDiffy.Rules;

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
