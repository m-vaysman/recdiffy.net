namespace RecDiffy.Rules;

internal sealed class SymbolMapFactory : Engine.IRuleFactory
{
    public string RuleId => "symbol.map";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new SymbolMapRule(args.Required("from"), args.Required("to"), args.Map("map"), args.Optional("passthrough") != "false");
}

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

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
        {
            row.SetNull(_to);
            return;
        }
        if (_map.TryGetValue(raw.Text, out var mapped))
            row.SetSymbol(_to, mapped, raw.Text);
        else if (_passthrough)
            row.SetSymbol(_to, raw.Text, raw.Text);
        else
            row.Diagnose("SymbolMap", DiagnosticSeverity.Invalid, _to, raw.Text);
    }
}
