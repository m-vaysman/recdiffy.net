using System.Globalization;

namespace RecDiffy.Rules;

// date.parse reads text only with a format that writes a year: one the profile declares, or the counterparty's format
// from reference data. The year never comes from the clock, and a format is never guessed (spec v3 §3.3, §10.1; #225).
// Replaced by PartyFormatDateRule (#275). DeclaredDateParsing.Apply rejects blank and typed dates when the formatBy party has no reference format.
// Friend tests still construct it; deleting a replaced rule is left to a human.
#pragma warning disable MA0182
[Obsolete("Replaced by PartyFormatDateRule: rejects blank and typed dates when formatBy party has no reference format. See #275.")]
internal sealed class DeclaredDateRule(string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture) : INormalizationRule
#pragma warning restore MA0182
{
    private readonly IReadOnlyList<string> _formats = DeclaredDateParsing.RequireYear(formats, culture);

    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = formatBy is null ? [] : [new FieldId(formatBy)];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = DeclaredDateParsing.Apply(row, from, to, _formats, formatBy, culture, RuleId);
}
