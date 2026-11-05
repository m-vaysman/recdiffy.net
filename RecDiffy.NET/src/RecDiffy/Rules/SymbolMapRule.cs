namespace RecDiffy.Rules;

internal sealed class SymbolMapRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly IReadOnlyDictionary<string, string> _map;
    private readonly bool _passthrough;

    public SymbolMapRule(string from, string to, IReadOnlyDictionary<string, string> map, bool passthrough)
    {
        _from = from;
        _to = to;
        _map = map;
        _passthrough = passthrough;
    }

    public string RuleId => "symbol.map";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row) =>
        row = SymbolMapping.Apply(row, _from, _to, _map, _passthrough, RuleId);
}
