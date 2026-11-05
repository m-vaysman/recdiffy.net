using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal static class QuantityAbsolute
{
    public static RowContext Apply(RowContext row, string from, string to, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var number = raw switch
        {
            decimal dec => dec,
            string text when decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ when row.Values.TryGetValue(from, out var cell) && !cell.IsNull => cell.Number,
            _ => (decimal?)null,
        };
        return number is { } quantity
            ? row.Write(to, CanonicalCell.FromDecimal(decimal.Abs(quantity)), ruleId, raw?.ToString(), decimal.Abs(quantity).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : row.Write(to, CanonicalCell.Null(FieldType.Decimal), ruleId, raw?.ToString(), null);
    }
}

internal sealed class QuantityAbsoluteRule(string from, string to) : INormalizationRule
{
    public string RuleId => "quantity.absolute";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = QuantityAbsolute.Apply(row, from, to, RuleId);
}
