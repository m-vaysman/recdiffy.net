using System.Data;

namespace RecDiffy.Extensions.Tests;

internal static class TableRead
{
    public static string[] ColumnNames(DataTable table) =>
        [.. table.Columns.Cast<DataColumn>().Select(column => column.ColumnName)];

    public static object[] Column(DataTable table, string name) =>
        [.. table.Rows.Cast<DataRow>().Select(row => row[name])];

    public static string[] Strings(DataTable table, string name) =>
        [.. Column(table, name).Select(value => value is DBNull ? "<null>" : (string)value)];

    public static string[] StatusPerGroup(DataTable table) =>
        [.. table.Rows.Cast<DataRow>()
            .GroupBy(row => (int)row["Group"])
            .OrderBy(group => group.Key)
            .Select(group => (string)group.First()["Status"])];

    public static string[] FirstRowPerGroup(DataTable table, string name) =>
        [.. table.Rows.Cast<DataRow>()
            .GroupBy(row => (int)row["Group"])
            .OrderBy(group => group.Key)
            .Select(group => (string)group.First()[name])];

    public static int[] RowsPerGroup(DataTable table) =>
        [.. table.Rows.Cast<DataRow>()
            .GroupBy(row => (int)row["Group"])
            .OrderBy(group => group.Key)
            .Select(group => group.Count())];
}
