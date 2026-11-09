using System.Data;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

public static class RunTables
{
    public static object? ForColumn(object? value, bool isDate, bool uses1904) =>
        (isDate, value) switch
        {
            (true, decimal serial) => ExcelDates.DateOf((double)serial, uses1904) ?? value,
            (true, double serial) => ExcelDates.DateOf(serial, uses1904) ?? value,
            _ => value,
        };

    public static DataTable ToDataTable(SourceTable table, IReadOnlySet<string> dateColumns)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(dateColumns);
        IReadOnlyList<bool> isDate = [.. table.Headers.Select(dateColumns.Contains)];
        var data = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var header in table.Headers)
            data.Columns.Add(header, typeof(object));
#pragma warning restore IDISP004
        foreach (var row in table.Rows)
        {
            object[] values = [.. row.Select((value, column) => ForColumn(value, isDate[column], table.Uses1904) ?? DBNull.Value)];
            data.Rows.Add(values);
        }

        return data;
    }
}
