namespace RecDiffy.Rules;

internal sealed class RequiredPresentRule : INormalizationRule
{
    private readonly IReadOnlyList<string> _fields;

    public RequiredPresentRule(IReadOnlyList<string> fields) => _fields = fields;

    public string RuleId => "required.present";

    public RulePhase Phase => RulePhase.Validate;

    public IReadOnlyList<FieldId> Reads => _fields.Select(name => new FieldId(name)).ToArray();

    public IReadOnlyList<FieldId> Writes { get; } = [];

    public void Apply(ref RowContext row) =>
        row = RequiredFields.Apply(row, _fields, RuleId);
}
