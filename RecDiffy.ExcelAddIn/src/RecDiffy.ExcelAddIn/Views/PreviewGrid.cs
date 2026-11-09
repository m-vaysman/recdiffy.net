using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Views;

public static class PreviewGrid
{
    public static readonly DependencyProperty TableProperty = DependencyProperty.RegisterAttached(
        "Table", typeof(PreviewTable), typeof(PreviewGrid), new PropertyMetadata(null, OnTableChanged));

    public static PreviewTable? GetTable(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TableProperty) is PreviewTable table ? table : null;
    }

    public static void SetTable(DependencyObject element, PreviewTable? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TableProperty, value);
    }

    private static void OnTableChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid)
            return;

        var table = e.NewValue is PreviewTable shown ? shown : null;
        var cellText = grid.TryFindResource("PreviewCellText") is Style style ? style : null;
        grid.Columns.Clear();
        foreach (var column in Columns(table, cellText))
            grid.Columns.Add(column);

        grid.ItemsSource = table is null ? null : (string[][])[.. table.Rows.Select(row => row.ToArray())];
    }

    private static IEnumerable<DataGridColumn> Columns(PreviewTable? table, Style? cellText) =>
        (table?.Headers ?? []).Select((header, index) => new DataGridTextColumn
        {
            Header = new TextBlock { Text = header },
            Binding = new Binding($"[{index}]"),
            ElementStyle = cellText,
        });
}
