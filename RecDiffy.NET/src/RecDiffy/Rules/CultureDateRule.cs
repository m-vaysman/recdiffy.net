using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class CultureDateRule(string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture) : INormalizationRule
{
    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = formatBy is null ? [] : [new FieldId(formatBy)];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) =>
        row = CultureDateParsing.Apply(row, from, to, formats, formatBy, culture, RuleId);
}
