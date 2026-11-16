using System.Data;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RecDiffy.Extensions;

/// <summary>
/// Renders rows as a styled HTML table: a dark sticky header, a colour bar and pill per status, zebra rows,
/// right-aligned numbers and monospace identifiers. The result is a <c>&lt;table&gt;</c> fragment; put
/// <see cref="Css"/> in the page that shows it.
/// </summary>
public static partial class HtmlTable
{
    /// <summary>The style sheet for the tables this class renders.</summary>
    public const string Css = """
        table.rec { border-collapse: separate; border-spacing: 0; margin: 6px 0 22px; font: 13px 'Segoe UI', system-ui, sans-serif;
                    color: #1f2328; background: #fff; border: 1px solid #d0d7de; border-radius: 10px; overflow: hidden;
                    box-shadow: 0 1px 3px rgba(31,35,40,.12); }
        table.rec caption { text-align: left; font-size: 15px; font-weight: 600; padding: 0 0 8px 2px; color: inherit; }
        table.rec th { position: sticky; top: 0; background: #24292f; color: #f6f8fa; text-align: left; font-weight: 600;
                       padding: 9px 14px; white-space: nowrap; }
        table.rec td { padding: 8px 14px; border-top: 1px solid #eaeef2; vertical-align: top; background: #fff; }
        table.rec tbody tr:nth-child(even) td { background: #f6f8fa; }
        table.rec tbody tr:hover td { background: #fff8c5; }
        table.rec td.num { text-align: right; white-space: nowrap; font-family: 'Cascadia Mono', Consolas, monospace;
                           font-variant-numeric: tabular-nums; }
        table.rec td.id { font-family: 'Cascadia Mono', Consolas, monospace; font-size: 12px; color: #57606a; }
        table.rec td.status { text-align: center; }
        table.rec .pill { display: inline-block; min-width: 26px; padding: 2px 8px; border-radius: 999px; font-size: 13px; }
        table.rec tr.ok     td:first-child { border-left: 4px solid #1a7f37; } table.rec tr.ok     .pill { background: #dafbe1; }
        table.rec tr.warn   td:first-child { border-left: 4px solid #bf8700; } table.rec tr.warn   .pill { background: #fff8c5; }
        table.rec tr.bad    td:first-child { border-left: 4px solid #cf222e; } table.rec tr.bad    .pill { background: #ffebe9; }
        table.rec tr.amb    td:first-child { border-left: 4px solid #8250df; } table.rec tr.amb    .pill { background: #fbefff; }
        table.rec tr.orphan td:first-child { border-left: 4px solid #0969da; } table.rec tr.orphan .pill { background: #ddf4ff; }
        """;

    [GeneratedRegex(@"^[\d,.\-\s]+(?:vs [\d,.\-\s]+)?$",RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Numeric();

    [GeneratedRegex(@"\d{6,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Identifier();

    /// <summary>
    /// Renders any sequence as a table with one column per public property. A <c>Status</c> column colours the row and
    /// shows a pill; numbers are right-aligned in a fixed-width font and long identifiers are in monospace.
    /// The properties are read by reflection, so the table is not trim-safe.
    /// </summary>
    /// <param name="rows">The rows to show.</param>
    /// <param name="caption">The table caption, or <see langword="null"/> for none.</param>
    public static string ToHtmlTable<T>(this IEnumerable<T> rows, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var properties = Properties.Of<T>();
        return Render(
            caption,
            [.. properties.Select(property => property.Name)],
            rows.Select(row => (object?[])[.. properties.Select(property => property.GetValue(row))]));
    }

    /// <summary>Renders a <see cref="DataTable"/> as a table. <see cref="DBNull"/> cells are empty.</summary>
    /// <param name="table">The table to show.</param>
    /// <param name="caption">The table caption. The default is the table's <see cref="DataTable.TableName"/>; an empty caption shows none.</param>
    public static string ToHtmlTable(this DataTable table, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = table.Columns.Cast<DataColumn>().ToArray();
        return Render(
            caption ?? table.TableName,
            [.. columns.Select(column => column.ColumnName)],
            table.AsEnumerable().Select(row => (object?[])[.. columns.Select(column => row[column] is DBNull ? null : row[column])]));
    }

    private static string Render(string? caption, string[] names, IEnumerable<object?[]> rows)
    {
        var captionHtml = caption is { Length: > 0 } ? $"<caption>{WebUtility.HtmlEncode(caption)}</caption>" : "";
        var header = string.Concat(names.Select(name => $"<th>{WebUtility.HtmlEncode(name)}</th>"));
        var statusIndex = Array.IndexOf(names, "Status");
        var body = string.Concat(rows.Select(values => Row(names, values, statusIndex)));

        return $"<table class=\"rec\">{captionHtml}<thead><tr>{header}</tr></thead><tbody>{body}</tbody></table>";
    }

    private static string Row(string[] names, object?[] values, int statusIndex)
    {
        var rowClass = statusIndex >= 0 && values[statusIndex] is string status ? Statuses.RowClass(status) : "";
        var cells = string.Concat(names.Select((name, index) => Cell(name, values[index])));

        return $"<tr class=\"{rowClass}\">{cells}</tr>";
    }

    private static string Cell(string name, object? value)
    {
        var text = Text(value);
        var cellClass = (name, value) switch
        {
            ("Status", _) => "status",
            (_, decimal or int or long or double) => "num",
            (_, string s) when Numeric().IsMatch(s) => "num",
            (_, string s) when Identifier().IsMatch(s) && s.Length < 120 => "id",
            _ => "",
        };
        var content = name == "Status"
            ? $"<span class=\"pill\">{Pill(text)}</span>"
            : WebUtility.HtmlEncode(text);

        return $"<td class=\"{cellClass}\">{content}</td>";
    }

    // The badge is ours and needs no encoding. HtmlEncode turns an emoji outside the BMP into a numeric entity.
    private static string Pill(string status) => Statuses.Badge(status) switch
    {
        "" => WebUtility.HtmlEncode(status),
        var badge => $"{badge} {WebUtility.HtmlEncode(status)}",
    };

    private static string Text(object? value) => value switch
    {
        null => "",
        DateOnly date => date.ToInvariant("yyyy-MM-dd"),
        DateTime time => time.ToInvariant("yyyy-MM-dd HH:mm:ss"),
        IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
