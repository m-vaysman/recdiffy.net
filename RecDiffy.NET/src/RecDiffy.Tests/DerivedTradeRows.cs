using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// A row as the Derive phase sees it: canonical numbers and text already parsed, so a derive rule can be tried on its own.
// A decimal becomes a decimal cell and a string a text cell. A null leaves the field unwritten.
internal static class DerivedTradeRows
{
    public static RowContext Canonical(this RowContext row, params (string Field, object? Value)[] cells) =>
        cells.Aggregate(
            row,
            (current, cell) => cell.Value switch
            {
                decimal number => current.Write(cell.Field, CanonicalCell.FromDecimal(number), "test", null, number.ToString(CultureInfo.InvariantCulture)),
                string text => current.Write(cell.Field, CanonicalCell.FromText(text), "test", text, text),
                _ => current,
            });
}
