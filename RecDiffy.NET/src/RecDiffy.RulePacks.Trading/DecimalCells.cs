using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// A canonical field's number, or null when the cell is absent, null or not a decimal. A text cell has no Number to read.
internal static class DecimalCells
{
    public static decimal? Of(RowContext row, string field) =>
        row.Values.TryGetValue(field, out var cell) && cell is { IsNull: false, Type: FieldType.Decimal } ? cell.Number : null;
}
