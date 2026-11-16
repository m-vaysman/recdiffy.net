using System.Data;
using System.Globalization;
using System.Net;

namespace RecDiffy.Extensions;

/// <summary>
/// Renders a <see cref="DataTable"/> as a flat data grid: number columns right-aligned with thousands separators,
/// a <c>Status</c> column as a coloured tag, and every row aligned on one text baseline. The result is a
/// <c>&lt;table class="grid"&gt;</c> fragment; put <see cref="Css"/> in the page that shows it. Text is culture-invariant.
/// </summary>
public static class DataGrid
{
    /// <summary>The style sheet for the grids this class renders.</summary>
    public const string Css = """
        table.grid { border-collapse: collapse; background: #fff; color: #1f2937; font: 12px 'Segoe UI', Arial, sans-serif; }
        table.grid caption { text-align: left; font-size: 14px; font-weight: 600; padding-bottom: 8px; }
        table.grid th { background: #f8fafc; color: #334155; font-weight: 600; text-align: left; white-space: nowrap; padding: 8px 12px;
                        border-bottom: 1px solid #cbd5e1; vertical-align: baseline; }
        table.grid td { padding: 6px 12px; border-bottom: 1px solid #e2e8f0; white-space: nowrap; vertical-align: baseline;
                        font-family: Consolas, 'Cascadia Mono', monospace; }
        table.grid .num { text-align: right; }
        table.grid .tag { display: inline-block; padding: 2px 6px; border-radius: 3px; font: 600 11px 'Segoe UI', Arial, sans-serif; }
        table.grid .tag.ok { color: #15803d; background: #dcfce7; }
        table.grid .tag.warn { color: #a16207; background: #fef9c3; }
        table.grid .tag.bad { color: #b91c1c; background: #fee2e2; }
        table.grid .tag.amb { color: #7e22ce; background: #f3e8ff; }
        table.grid .tag.orphan { color: #c2410c; background: #ffedd5; }
        """;

    // A column is a number column by its data type. Its decimals share one scale, the largest in the column,
    // so the decimal points line up.
    private sealed record Column(DataColumn Source, bool Number, bool Status, int Scale);

    /// <summary>
    /// Renders <paramref name="table"/> as a grid. Number columns (<see cref="decimal"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>) are right-aligned with thousands separators: decimals keep the column's largest scale, whole numbers
    /// have none, doubles have two. Dates are <c>yyyy-MM-dd</c>. <see cref="DBNull"/> cells are empty.
    /// </summary>
    /// <param name="table">The table to show.</param>
    /// <param name="caption">The caption. The default is the table's <see cref="DataTable.TableName"/>; an empty caption shows none.</param>
    public static string ToDataGridHtml(this DataTable table, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        var rows = table.Rows.Cast<DataRow>().ToArray();
        var columns = table.Columns.Cast<DataColumn>().Select(column => Describe(column, rows)).ToArray();
        var title = caption ?? table.TableName;

        var captionHtml = title is { Length: > 0 } ? $"<caption>{WebUtility.HtmlEncode(title)}</caption>" : "";
        var header = string.Concat(columns.Select(Header));
        var body = string.Concat(rows.Select(row => $"<tr>{string.Concat(columns.Select(column => Cell(column, row[column.Source])))}</tr>"));

        return $"<table class=\"grid\">{captionHtml}<thead><tr>{header}</tr></thead><tbody>{body}</tbody></table>";
    }

    private static Column Describe(DataColumn column, DataRow[] rows) =>
        new(
            column,
            Type.GetTypeCode(column.DataType) is TypeCode.Decimal or TypeCode.Int32 or TypeCode.Int64 or TypeCode.Double,
            column.ColumnName == "Status",
            rows.Select(row => row[column]).OfType<decimal>().Select(number => (int)number.Scale).DefaultIfEmpty(0).Max());

    private static string Header(Column column) =>
        column.Number
            ? $"<th class=\"num\">{WebUtility.HtmlEncode(column.Source.ColumnName)}</th>"
            : $"<th>{WebUtility.HtmlEncode(column.Source.ColumnName)}</th>";

    private static string Cell(Column column, object value) => (column, value) switch
    {
        ({ Number: true }, DBNull) => "<td class=\"num\"></td>",
        (_, DBNull) => "<td></td>",
        ({ Status: true }, string status) => $"<td>{Tag(status)}</td>",
        ({ Number: true }, _) => $"<td class=\"num\">{WebUtility.HtmlEncode(Text(value, column.Scale))}</td>",
        _ => $"<td>{WebUtility.HtmlEncode(Text(value, column.Scale))}</td>",
    };

    private static string Tag(string status) => Statuses.RowClass(status) switch
    {
        "" => $"<span class=\"tag\">{WebUtility.HtmlEncode(status)}</span>",
        var kind => $"<span class=\"tag {kind}\">{WebUtility.HtmlEncode(status)}</span>",
    };

    private static string Text(object value, int scale) => value switch
    {
        decimal number => number.ToInvariant($"N{scale.ToInvariant()}"),
        int number => number.ToInvariant("N0"),
        long number => number.ToInvariant("N0"),
        double number => number.ToInvariant("N2"),
        DateOnly date => date.ToInvariant("yyyy-MM-dd"),
        DateTime time => time.ToInvariant("yyyy-MM-dd HH:mm:ss"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
