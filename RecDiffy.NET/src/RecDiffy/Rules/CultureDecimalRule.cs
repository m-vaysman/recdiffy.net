using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class CultureDecimalRule(string from, string to, int scale, MidpointMode midpoint, NumberFormatInfo format) : INormalizationRule
{
    public string RuleId => "decimal.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row) => row = Parse(row);

    // A float or double is never converted: any conversion is a choice the user has to make before the value arrives.
    private RowContext Parse(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        return raw switch
        {
            double or float or Half => Reject(row, "FloatValue", rawText),
            _ => raw.ReadNumber(format).Convert(
                value: number => WriteRounded(row, number, rawText),
                missing: () => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, null, null),
                failed: () => Reject(row, "DecimalParse", rawText)),
        };
    }

    private RowContext Reject(RowContext row, string code, string? rawText) =>
        row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, rawText, null)
            .Diagnose(code, DiagnosticSeverity.Invalid, RuleId, to, rawText);

    private RowContext WriteRounded(RowContext row, decimal number, string? rawText)
    {
        var rounded = Rounding.Round(number, scale, midpoint);
        return row.Write(to, CanonicalCell.FromDecimal(rounded), RuleId, rawText, rounded.ToString(CultureInfo.InvariantCulture));
    }
}
