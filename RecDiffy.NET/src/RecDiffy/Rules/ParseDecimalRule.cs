using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class ParseDecimalRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "decimal.parse";

    public RulePhase Phase => RulePhase.Parse;

    // The source column is raw text, so the read set stays empty.
    // Listing it makes compile report FieldReadBeforeWrite.
    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) => row = Parse(row);

    private RowContext Parse(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = raw?.ToString();
        return raw.CanBeNumber().Convert(
            value: number => WriteRounded(row, number, rawText),
            missing: () => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, null, null),
            failed: () => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, rawText, null)
                .Diagnose("DecimalParse", DiagnosticSeverity.Invalid, RuleId, to, rawText));
    }

    private RowContext WriteRounded(RowContext row, decimal number, string? rawText)
    {
        var rounded = Rounding.Round(number, scale, midpoint);
        return row.Write(to, CanonicalCell.FromDecimal(rounded), RuleId, rawText, rounded.ToString(CultureInfo.InvariantCulture));
    }
}
