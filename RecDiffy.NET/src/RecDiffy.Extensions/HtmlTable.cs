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
                       padding: 9px 14px; white-space: nowrap; vertical-align: baseline; }
        table.rec th.num { text-align: right; }
        table.rec td { padding: 8px 14px; border-top: 1px solid #eaeef2; vertical-align: baseline; background: #fff; }
        table.rec tbody tr:nth-child(even) td { background: #f6f8fa; }
        table.rec tbody tr:hover td { background: #fff8c5; }
        table.rec td.num { text-align: right; white-space: nowrap; font-family: 'Cascadia Mono', Consolas, monospace;
                           font-variant-numeric: tabular-nums; }
        table.rec td.id { font-family: 'Cascadia Mono', Consolas, monospace; font-size: 12px; color: #57606a; }
        table.rec td.status { text-align: left; }
        table.rec .pill { display: inline-block; min-width: 26px; padding: 2px 8px; border-radius: 999px; font-size: 13px; white-space: nowrap; }
        table.rec tr.ok     td:first-child { border-left: 4px solid #1a7f37; } table.rec tr.ok     .pill { background: #dafbe1; }
        table.rec tr.warn   td:first-child { border-left: 4px solid #bf8700; } table.rec tr.warn   .pill { background: #fff8c5; }
        table.rec tr.bad    td:first-child { border-left: 4px solid #cf222e; } table.rec tr.bad    .pill { background: #ffebe9; }
        table.rec tr.amb    td:first-child { border-left: 4px solid #8250df; } table.rec tr.amb    .pill { background: #fbefff; }
        table.rec tr.orphan td:first-child { border-left: 4px solid #0969da; } table.rec tr.orphan .pill { background: #ddf4ff; }
        """;

#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^[\d,.\-\s]+(?:vs [\d,.\-\s]+)?$",RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Numeric { get; }

    [GeneratedRegex(@"\d{6,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Identifier { get; }
#else
    [GeneratedRegex(@"^[\d,.\-\s]+(?:vs [\d,.\-\s]+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumericPattern();

    [GeneratedRegex(@"\d{6,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentifierPattern();

    private static Regex Numeric => NumericPattern();

    private static Regex Identifier => IdentifierPattern();
#endif

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

    private enum Kind
    {
        Plain,
        Numeric,
        Identifier,
        Status,
    }

    // A column is one kind as a whole, decided from every cell in it, so its header and cells always align alike.
    private sealed record Column(string Name, Kind Kind, int Fraction);

    private static string Render(string? caption, string[] names, IEnumerable<object?[]> rows)
    {
        object?[][] cells = [.. rows];
        var columns = names.Select((name, index) => Describe(name, cells.Select(row => row[index]))).ToArray();
        var statusIndex = Array.IndexOf(names, "Status");

        var captionHtml = caption is { Length: > 0 } ? $"<caption>{WebUtility.HtmlEncode(caption)}</caption>" : "";
        var header = string.Concat(columns.Select(Header));
        var body = string.Concat(cells.Select(values => Row(columns, values, statusIndex)));

        return $"<table class=\"rec\">{captionHtml}<thead><tr>{header}</tr></thead><tbody>{body}</tbody></table>";
    }

    private static Column Describe(string name, IEnumerable<object?> values)
    {
        object[] present = [.. values.OfType<object>().Where(value => Text(value) is not "")];
        var kind = (name, present) switch
        {
            ("Status", _) => Kind.Status,
            (_, { Length: > 0 }) when present.All(IsNumber) => Kind.Numeric,
            (_, { Length: > 0 }) when present.All(IsIdentifier) => Kind.Identifier,
            _ => Kind.Plain,
        };
        var fraction = kind is Kind.Numeric ? present.Max(value => FractionDigits(value, Text(value))) : 0;

        return new Column(name, kind, fraction);
    }

    private static bool IsNumber(object value) => value switch
    {
        decimal or double or float or int or long or short or byte or sbyte or ushort or uint or ulong => true,
        string text => Numeric.IsMatch(text),
        _ => false,
    };

    private static bool IsIdentifier(object value) =>
        value is string { Length: < 120 } text && Identifier.IsMatch(text);

    private static bool IsPlainNumber(string text) =>
        text.All(character => char.IsAsciiDigit(character) || character is '-' or '.');

    // Only a fractional number is padded. Zeros are added after the point, so the value does not change.
    private static int FractionDigits(object value, string text) => (value, text.IndexOf('.', StringComparison.Ordinal)) switch
    {
        (decimal or double or float, >= 0 and var point) when IsPlainNumber(text) => text.Length - point - 1,
        _ => 0,
    };

    private static string Number(object? value, string text, int fraction) => (value, text.IndexOf('.', StringComparison.Ordinal)) switch
    {
        (decimal or double or float, < 0) when fraction > 0 && IsPlainNumber(text) => $"{text}.{new string('0', fraction)}",
        (decimal or double or float, >= 0 and var point) when IsPlainNumber(text) => $"{text}{new string('0', Math.Max(0, fraction - (text.Length - point - 1)))}",
        _ => text,
    };

    private static string Header(Column column) =>
        column.Kind is Kind.Numeric
            ? $"<th class=\"num\">{WebUtility.HtmlEncode(column.Name)}</th>"
            : $"<th>{WebUtility.HtmlEncode(column.Name)}</th>";

    private static string Row(Column[] columns, object?[] values, int statusIndex)
    {
        var rowClass = statusIndex >= 0 && values[statusIndex] is string status ? Statuses.RowClass(status) : "";
        var cells = string.Concat(columns.Select((column, index) => Cell(column, values[index])));

        return $"<tr class=\"{rowClass}\">{cells}</tr>";
    }

    private static string Cell(Column column, object? value)
    {
        var text = Text(value);
        var cellClass = column.Kind switch
        {
            Kind.Status => "status",
            Kind.Numeric => "num",
            Kind.Identifier => "id",
            _ => "",
        };
        var content = column.Kind switch
        {
            Kind.Status => $"<span class=\"pill\">{Pill(text)}</span>",
            Kind.Numeric => WebUtility.HtmlEncode(Number(value, text, column.Fraction)),
            _ => WebUtility.HtmlEncode(text),
        };

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
