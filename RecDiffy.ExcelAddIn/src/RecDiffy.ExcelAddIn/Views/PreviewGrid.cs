using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Views;

public static class PreviewGrid
{
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.RegisterAttached(
        "Preview", typeof(SourcePreview), typeof(PreviewGrid), new PropertyMetadata(null, OnPreviewChanged));

    public static SourcePreview? GetPreview(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(PreviewProperty) is SourcePreview preview ? preview : null;
    }

    public static void SetPreview(DependencyObject element, SourcePreview? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PreviewProperty, value);
    }

    private static void OnPreviewChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid)
            return;

        var preview = e.NewValue is SourcePreview shown ? shown : null;
        var cellText = grid.TryFindResource("PreviewCellText") is Style style ? style : null;
        grid.Columns.Clear();
        foreach (var column in Columns(preview, cellText))
            grid.Columns.Add(column);

        grid.ItemsSource = preview is null ? null : (string[][])[.. preview.Rows.Select(row => row.ToArray())];
    }

    private static IEnumerable<DataGridColumn> Columns(SourcePreview? preview, Style? cellText) =>
        (preview?.Headers ?? []).Select((header, index) => new DataGridTextColumn
        {
            Header = new TextBlock { Text = header },
            Binding = new Binding($"[{index}]"),
            ElementStyle = cellText,
        });
}
