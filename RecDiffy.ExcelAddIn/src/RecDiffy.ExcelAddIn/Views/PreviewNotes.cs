using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Views;

public static class PreviewNotes
{
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.RegisterAttached(
        "Preview", typeof(SourcePreview), typeof(PreviewNotes), new PropertyMetadata(null, OnPreviewChanged));

    private static readonly Brush Highlight = Frozen(new SolidColorBrush(Color.FromRgb(0xFC, 0xEF, 0xC7)));

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

        grid.LoadingRow -= OnLoadingRow;
        grid.LoadingRow += OnLoadingRow;
    }

    private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Loaded -= OnRowLoaded;
        e.Row.Loaded += OnRowLoaded;
    }

    private static void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row || ItemsControl.ItemsControlFromItemContainer(row) is not DataGrid grid)
            return;

        var preview = GetPreview(grid);
        var index = row.GetIndex();
        foreach (var (column, position) in grid.Columns.Select((column, position) => (column, position)))
        {
            if (column.GetCellContent(row)?.Parent is DataGridCell cell)
                Mark(cell, CellNotes.At(preview, index, position));
        }
    }

    private static void Mark(DataGridCell cell, string? note)
    {
        if (note is null)
        {
            cell.ClearValue(Control.BackgroundProperty);
            cell.ClearValue(FrameworkElement.ToolTipProperty);
            cell.ClearValue(AutomationProperties.HelpTextProperty);
            return;
        }

        cell.Background = Highlight;
        cell.ToolTip = new ToolTip { Content = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 } };
        AutomationProperties.SetHelpText(cell, note);
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
