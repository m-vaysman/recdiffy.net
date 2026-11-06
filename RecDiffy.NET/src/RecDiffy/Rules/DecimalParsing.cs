using System.Globalization;

namespace RecDiffy.Rules;

internal static class DecimalParsing
{
    public static RowContext Apply(RowContext row, string from, string to, int scale, MidpointMode midpoint, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        return Read(raw) switch
        {
            { Missing: true } => row.Write(to, CanonicalCell.Null(FieldType.Decimal), ruleId, null, null),
            { Ok: true, Value: var number } => WriteNumber(row, to, number, scale, midpoint, ruleId, raw?.ToString()),
            _ => row.Write(to, CanonicalCell.Null(FieldType.Decimal), ruleId, raw?.ToString(), null)
                .Diagnose("DecimalParse", DiagnosticSeverity.Invalid, ruleId, to, raw?.ToString()),
        };
    }

    private static RowContext WriteNumber(RowContext row, string to, decimal number, int scale, MidpointMode midpoint, string ruleId, string? raw)
    {
        var rounded = Rounding.Round(number, scale, midpoint);
        return row.Write(to, CanonicalCell.FromDecimal(rounded), ruleId, raw, rounded.ToString(CultureInfo.InvariantCulture));
    }

    private static Parsed Read(object? raw) => raw switch
    {
        null or "" => Parsed.Blank,
        decimal value => Parsed.Of(value),
        double value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),
        float value when Rounding.TryFromDouble(value, out var number) => Parsed.Of(number),
        int value => Parsed.Of(value),
        long value => Parsed.Of(value),
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => Parsed.Of(parsed),
        _ => Parsed.Failed,
    };

    private readonly record struct Parsed(bool Ok, bool Missing, decimal Value)
    {
        public static Parsed Blank { get; } = new(true, true, 0m);

        public static Parsed Failed { get; } = new(false, false, 0m);

        public static Parsed Of(decimal value) => new(true, false, value);
    }
}
