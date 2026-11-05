namespace RecDiffy.Rules;

internal sealed class IdentifierMapFactory : Engine.IRuleFactory
{
    public string RuleId => "identifier.map";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new IdentifierMapRule(args.Required("from"), args.Required("to"), args.Optional("scheme") ?? "id", args.Optional("scope") ?? "", args.Optional("scopeFrom"));
}

internal sealed class IdentifierMapRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly string _scheme;
    private readonly string _scope;
    private readonly string? _scopeFrom;
    public IdentifierMapRule(string from, string to, string scheme, string scope, string? scopeFrom)
    {
        _from = from;
        _to = to;
        _scheme = scheme;
        _scope = scope;
        _scopeFrom = scopeFrom;
    }

    public string RuleId => "identifier.map";
    public RulePhase Phase => RulePhase.Identify;
    public IReadOnlyList<FieldId> Reads => _scopeFrom is null ? [] : [new FieldId(_scopeFrom)];
    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
        {
            row.SetNull(_to);
            return;
        }
        var scope = _scope;
        if (_scopeFrom is not null && row.TryCanonical(_scopeFrom, out var party))
            scope = row.State.Pool.Id(party.Interned).Value;
        row.SetIdentifier(_to, _scheme, scope, raw.Text.Trim(), raw.Text);
    }
}
