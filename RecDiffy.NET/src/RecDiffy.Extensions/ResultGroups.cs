using System.Data;
using System.Globalization;
using System.Reflection;
using RecDiffy.Results;

namespace RecDiffy.Extensions;

internal sealed record ResultGroup<TLeft, TRight>(int Group, string Status, IReadOnlyList<TLeft> Left, IReadOnlyList<TRight> Right, string Diffs);

internal static class ResultGroups
{
    public static ResultGroup<TLeft, TRight>[] Of<TLeft, TRight>(RecResult<TLeft, TRight> result)
    {
        var matched = result.Matched
            .Select(match => (Status: match.Outcome.ToString(), match.Left, match.Right, Diffs: Describe(result.Result, match.Diffs)));
        var ambiguous = result.Ambiguous
            .Select(group => (Status: Statuses.Ambiguous, group.Left, group.Right, Diffs: ""));
        var leftOnly = result.LeftOnly
            .Select(left => (Status: Statuses.LeftOnly, Left: (IReadOnlyList<TLeft>)[left], Right: (IReadOnlyList<TRight>)[], Diffs: ""));
        var rightOnly = result.RightOnly
            .Select(right => (Status: Statuses.RightOnly, Left: (IReadOnlyList<TLeft>)[], Right: (IReadOnlyList<TRight>)[right], Diffs: ""));

        return
        [
            .. matched
                .Concat(ambiguous)
                .Concat(leftOnly)
                .Concat(rightOnly)
                .Select((group, index) => new ResultGroup<TLeft, TRight>(index + 1, group.Status, group.Left, group.Right, group.Diffs)),
        ];
    }

    private static string Describe(IReconciliationResult result, IEnumerable<FieldDiff> diffs) =>
        string.Join(" · ", diffs.Select(diff => $"{diff.Field.Name} {result.Format(diff.Left)} → {result.Format(diff.Right)}"));
}

internal static class Properties
{
    public static PropertyInfo[] Of<T>() =>
        [.. typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)];

    public static Type ColumnType(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

    public static object Value<T>(PropertyInfo property, IReadOnlyList<T> items, int index) =>
        index < items.Count && property.GetValue(items[index]) is { } value ? value : DBNull.Value;
}

internal static class Tables
{
    public static DataTable Build(string tableName, IEnumerable<(string Name, Type Type)> columns, IEnumerable<object?[]> rows)
    {
        var table = new DataTable(tableName) { Locale = CultureInfo.InvariantCulture };
        table.Columns.AddRange([.. columns.Select(column => new DataColumn(column.Name, column.Type))]);
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }
}
