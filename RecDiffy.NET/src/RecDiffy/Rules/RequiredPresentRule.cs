namespace RecDiffy.Rules;

internal sealed class RequiredPresentFactory : Engine.IRuleFactory
{
    public string RuleId => "required.present";
    public RulePhase Phase => RulePhase.Validate;
    public INormalizationRule Bind(RuleArguments args) => new RequiredPresentRule(args.List("fields"));
}

internal sealed class RequiredPresentRule : INormalizationRule
{
    private readonly IReadOnlyList<string> _fields;
    public RequiredPresentRule(IReadOnlyList<string> fields) => _fields = fields;
    public string RuleId => "required.present";
    public RulePhase Phase => RulePhase.Validate;
    public IReadOnlyList<FieldId> Reads => _fields.Select(f => new FieldId(f)).ToArray();
    public IReadOnlyList<FieldId> Writes { get; } = [];

    public void Apply(ref RowContext row)
    {
        foreach (var field in _fields)
        {
            if (!row.TryCanonical(field, out _))
                row.Diagnose("RequiredMissing", DiagnosticSeverity.Invalid, field);
        }
    }
}
