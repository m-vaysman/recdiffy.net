namespace RecDiffy.Rules;

// Replaced by CultureDateRule (#158). It parses with the invariant culture whatever the profile declares. date.parse no longer builds it.
[Obsolete("Replaced by CultureDateRule: parses with the invariant culture whatever the profile declares. See #158.")]
internal sealed class DateParseRule(string from, string to, IReadOnlyList<string> formats, string? formatBy) : INormalizationRule
{
    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    // formatBy is a canonical field, so it is a read.
    // The source column stays out; listing it makes compile report FieldReadBeforeWrite.
    public IReadOnlyList<FieldId> Reads { get; } = formatBy is null ? [] : [new FieldId(formatBy)];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = DateParsing.Apply(row, from, to, formats, formatBy, RuleId);
}
