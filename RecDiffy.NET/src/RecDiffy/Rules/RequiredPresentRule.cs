namespace RecDiffy.Rules;

internal sealed class RequiredPresentRule(IReadOnlyList<string> fields) : INormalizationRule
{
    public string RuleId => "required.present";

    public RulePhase Phase => RulePhase.Validate;

    public IReadOnlyList<FieldId> Reads => [.. fields.Select(name => new FieldId(name))];

    public IReadOnlyList<FieldId> Writes { get; } = [];

    public void Apply(ref RowContext row) =>
        row = RequiredFields.Apply(row, fields, RuleId);
}
