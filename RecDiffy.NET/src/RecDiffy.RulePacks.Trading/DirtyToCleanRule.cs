using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Spec v3.1 §3.3: clean = dirty - accrued / quantity * 100, rounded to the price field's scale with the profile midpoint.
// A dirty quote that cannot be cleaned (no accrued, or no quantity) is an unreadable price: PriceParse, so the row is quarantined.
internal sealed class DirtyToCleanRule(string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "price.dirtyToClean";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } = [new(to), new(TradeFields.Accrued), new(TradeFields.Quantity)];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = row.IsNull(to) ? row : Clean(row, row.Values[to].Number);

    private RowContext Clean(RowContext row, decimal dirty) =>
        CleanPrice(row, dirty) switch
        {
            { } clean => row.Write(to, CanonicalCell.FromDecimal(clean), RuleId, row.Display(to), clean.ToString(CultureInfo.InvariantCulture)),
            null => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, row.Display(to), null)
                .Diagnose("PriceParse", DiagnosticSeverity.Invalid, RuleId, to, row.Display(to)),
        };

    private decimal? CleanPrice(RowContext row, decimal dirty) =>
        (Number(row, TradeFields.Accrued), Number(row, TradeFields.Quantity)) switch
        {
            ({ } accrued, { } quantity) when decimal.Abs(quantity) is > 0 and var face =>
                CheckedDecimal.Subtract(dirty, CheckedDecimal.Divide(CheckedDecimal.Multiply(accrued, 100m), face)) is { } clean
                    ? Rounding.Round(clean, scale, midpoint)
                    : null,
            _ => null,
        };

    private static decimal? Number(RowContext row, string field) => row.IsNull(field) ? null : row.Values[field].Number;
}
