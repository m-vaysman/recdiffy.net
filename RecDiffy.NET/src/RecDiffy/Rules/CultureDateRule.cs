using System.Globalization;

namespace RecDiffy.Rules;

// Replaced by DeclaredDateRule (#225). A format with no year takes the year from the clock, and no format guesses one.
// date.parse no longer builds it.
[Obsolete("Replaced by DeclaredDateRule: a format with no year takes the year from the clock, and no format guesses one. See #225.")]
internal sealed class CultureDateRule(string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture) : INormalizationRule
{
    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = formatBy is null ? [] : [new FieldId(formatBy)];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = CultureDateParsing.Apply(row, from, to, formats, formatBy, culture, RuleId);
}
