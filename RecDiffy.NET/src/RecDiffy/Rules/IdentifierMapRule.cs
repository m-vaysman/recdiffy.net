namespace RecDiffy.Rules;

internal sealed class IdentifierMapRule(string from, string to, string scheme, string scope) : INormalizationRule
{
    public string RuleId => "identifier.map";

    public RulePhase Phase => RulePhase.Identify;

    // The source column is raw text, so the read set stays empty.
    // Listing it makes compile report FieldReadBeforeWrite.
    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = IdentifierMapping.Apply(row, from, to, scheme, scope, RuleId);
}
