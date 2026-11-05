namespace RecDiffy.Rules;

internal sealed class IdentifierMapRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly string _scheme;
    private readonly string _scope;

    public IdentifierMapRule(string from, string to, string scheme, string scope)
    {
        _from = from;
        _to = to;
        _scheme = scheme;
        _scope = scope;
    }

    public string RuleId => "identifier.map";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row) =>
        row = IdentifierMapping.Apply(row, _from, _to, _scheme, _scope, RuleId);
}
