using System.Data;
using RecDiffy.Results;

namespace RecDiffy.Extensions;

/// <summary>A side-by-side view as a <see cref="DataTable"/> or as an HTML table.</summary>
public static class SideBySideExtensions
{
    private static readonly (string Name, Func<SideBySideCell, decimal> Read)[] Parts =
    [
        ("Left", cell => cell.Left),
        ("Right", cell => cell.Right),
        ("Delta", cell => cell.Delta),
        ("LeftOnly", cell => cell.LeftOnly),
        ("RightOnly", cell => cell.RightOnly),
        ("MatchedDelta", cell => cell.MatchedDelta),
    ];

    /// <summary>
    /// One row per line of the view: <c>Row</c>, <c>Column</c>, then six decimal columns per measure
    /// (<c>Qty.Left</c>, <c>Qty.Right</c>, <c>Qty.Delta</c>, <c>Qty.LeftOnly</c>, <c>Qty.RightOnly</c>, <c>Qty.MatchedDelta</c>).
    /// A measure missing from a line is <see cref="DBNull"/>.
    /// </summary>
    public static DataTable ToSideBySideTable(this ISideBySideView view, string tableName = "SideBySide")
    {
        ArgumentNullException.ThrowIfNull(view);

        var measures = view.Lines
            .SelectMany(line => line.Cells.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var valueColumns = measures
            .SelectMany(measure => Parts.Select(part => (Name: $"{measure}.{part.Name}", Measure: measure, part.Read)))
            .ToArray();

        var columns = new (string Name, Type Type)[] { ("Row", typeof(string)), ("Column", typeof(string)) }
            .Concat(valueColumns.Select(column => (column.Name, Type: typeof(decimal))));

        var rows = view.Lines
            .Select(line => (object?[])
            [
                line.Row,
                line.Column,
                .. valueColumns.Select(column => line.Cells.TryGetValue(column.Measure, out var cell)
                    ? column.Read(cell)
                    : (object)DBNull.Value),
            ]);

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>The <see cref="ToSideBySideTable(ISideBySideView, string)"/> table as HTML. Add <see cref="HtmlTable.Css"/> to the page.</summary>
    public static string ToSideBySideHtml(this ISideBySideView view, string caption = "SideBySide")
    {
        using var table = view.ToSideBySideTable(caption);
        return table.ToHtmlTable(caption);
    }
}
