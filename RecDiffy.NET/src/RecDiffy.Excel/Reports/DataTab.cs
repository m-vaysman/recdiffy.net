using System.Collections.Immutable;
using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

/// <summary>A column of a data tab. The value function receives the item and the sheet row it lands on.</summary>
internal sealed record DataColumn<T>(string Key, Func<T, int, object?> Value, string? Format = null, double Width = 12, Func<T, string?>? FormatFor = null)
{
    public static DataColumn<T> Of(string key, Func<T, object?> value, string? format = null, double width = 12) =>
        new(key, (item, _) => value(item), format, width);

    public string? FormatOf(T item) => FormatFor?.Invoke(item) ?? Format;
}

/// <summary>A data tab and the addresses report formulas use to link to it.</summary>
internal sealed record DataTab(string Name, Sheet Sheet, ImmutableDictionary<string, int> Columns, int FirstRow, int Count)
{
    public int LastRow => Math.Max(FirstRow, FirstRow + Count - 1);

    public int RowOf(int index) => FirstRow + index;

    public string Column(string key) => Address.Column(Columns[key]);

    /// <summary>An absolute reference to one value, for example Result_Entries!$Q$12.</summary>
    public string Cell(string key, int index) => $"{Address.Sheet(Name)}!{Address.Absolute(RowOf(index), Columns[key])}";

    /// <summary>An absolute reference to a whole column of values, for COUNTIFS and SUMIFS.</summary>
    public string Range(string key) => $"{Address.Sheet(Name)}!${Column(key)}${FirstRow.ToInvariant()}:${Column(key)}${LastRow.ToInvariant()}";

    public static DataTab Build<T>(string name, IReadOnlyList<string> notes, IReadOnlyList<DataColumn<T>> columns, IReadOnlyList<T> items)
    {
        var header = notes.Count == 0 ? 1 : notes.Count + 2;
        var first = header + 1;
        var sheet = Sheet.Empty(name)
            .Fold(notes, (current, note, index) => current.Put(index + 1, 1, note, index == 0 ? Styles.Section : Styles.Note))
            .Header(header, [.. columns.Select(column => column.Key)])
            .Fold(items, (current, item, index) => columns
                .Select((column, position) => (column, position))
                .Aggregate(current, (inner, pair) => inner.Put(
                    first + index,
                    pair.position + 1,
                    pair.column.Value(item, first + index),
                    pair.column.FormatOf(item) is { } format ? Styles.Number(format) : Styles.Plain)))
            .Widths(columns.Select(column => column.Width))
            .FreezeAt(first, 1)
            .Filter(header, 1, first + items.Count - 1, columns.Count)
            .Gridlines(true);
        return new DataTab(
            name,
            sheet,
            columns.Select((column, index) => (column.Key, index)).ToImmutableDictionary(pair => pair.Key, pair => pair.index + 1, StringComparer.Ordinal),
            first,
            items.Count);
    }
}
