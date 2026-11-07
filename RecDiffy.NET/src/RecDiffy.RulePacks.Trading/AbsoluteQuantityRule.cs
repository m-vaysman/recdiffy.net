using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal sealed class AbsoluteQuantityRule(string from, string to) : INormalizationRule
{
    public string RuleId => "quantity.absolute";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = Absolute(row);

    private RowContext Absolute(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = raw?.ToString();
        var earlier = row.Values.TryGetValue(from, out var cell) && !cell.IsNull ? cell.Number : (decimal?)null;
        return raw.CanBeNumber().Convert(
            value: quantity => WriteAbsolute(row, quantity, rawText),
            missing: () => earlier is { } quantity ? WriteAbsolute(row, quantity, rawText) : WriteNull(row, rawText),
            failed: () => earlier is { } quantity
                ? WriteAbsolute(row, quantity, rawText)
                : WriteNull(row, rawText).Diagnose("DecimalParse", DiagnosticSeverity.Invalid, RuleId, to, rawText));
    }

    private RowContext WriteAbsolute(RowContext row, decimal quantity, string? rawText)
    {
        var absolute = decimal.Abs(quantity);
        return row.Write(to, CanonicalCell.FromDecimal(absolute), RuleId, rawText, absolute.ToString(CultureInfo.InvariantCulture));
    }

    private RowContext WriteNull(RowContext row, string? rawText) =>
        row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, rawText, null);
}
