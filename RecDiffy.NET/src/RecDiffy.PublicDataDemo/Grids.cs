using System.Data;
using System.Globalization;

namespace RecDiffy.PublicDataDemo;

/// <summary>Small <see cref="DataTable"/>s for the page's own strips and histories, rendered with the same grid as the engine's tables.</summary>
internal static class Grids
{
    public static DataTable Build(string name, IReadOnlyList<(string Name, Type Type)> columns, IEnumerable<object?[]> rows)
    {
        var table = new DataTable(name) { Locale = CultureInfo.InvariantCulture };
        table.Columns.AddRange([.. columns.Select(column => new DataColumn(column.Name, column.Type))]);
        foreach (var row in rows)
            table.Rows.Add([.. row.Select(value => value ?? DBNull.Value)]);
        return table;
    }

    /// <summary>One row of facts, one column each: the strip above each tab's grids.</summary>
    public static DataTable Strip(IReadOnlyList<(string Name, object Value)> facts) =>
        Build("Run", [.. facts.Select(fact => (fact.Name, fact.Value.GetType()))], [[.. facts.Select(fact => fact.Value)]]);

    /// <summary>The rows of <paramref name="table"/> whose <paramref name="column"/> passes <paramref name="keep"/>, in their order.</summary>
    public static DataTable Where(DataTable table, string column, Func<string, bool> keep)
    {
        var kept = table.Clone();
        foreach (var row in table.AsEnumerable().Where(row => row[column] is string value && keep(value)))
            kept.ImportRow(row);
        return kept;
    }

    /// <summary>The rows of <paramref name="table"/> whose key, read from <paramref name="column"/>, is in <paramref name="keys"/>, in the order of the keys.</summary>
    public static DataTable Pick(DataTable table, string column, IReadOnlyList<string> keys, Func<string, string> keyOf)
    {
        var byKey = table.AsEnumerable()
            .SelectMany(row => row[column] is string text ? [(Key: keyOf(text), Row: row)] : Array.Empty<(string Key, DataRow Row)>())
            .ToLookup(pair => pair.Key, pair => pair.Row, StringComparer.Ordinal);
        var kept = table.Clone();
        foreach (var row in keys.SelectMany(key => byKey[key]))
            kept.ImportRow(row);
        return kept;
    }
}
