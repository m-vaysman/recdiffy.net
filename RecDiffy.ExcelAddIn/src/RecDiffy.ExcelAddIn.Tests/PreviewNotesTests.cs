using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn.Tests;

public class PreviewNotesTests
{
    private static readonly SourcePreview Sample = new(["Ref", "Quantity", "Book"], [["T-1\u200B", 10.5, "  FX"], ["T-2", 7.0, "Rates"]], 2);

    [Fact]
    public void A_cell_with_a_note_is_highlighted_and_carries_the_note_as_its_tooltip_and_help_text() => OnStaThread(() =>
    {
        var grid = new DataGrid();
        PreviewGrid.SetTable(grid, Sample.Render(CultureInfo.InvariantCulture));
        PreviewNotes.SetPreview(grid, Sample);
        using var host = Shown(grid);

        Assert.Equal(CellNotes.For("T-1\u200B"), NoteOf(Cell(grid, 0, 0)));
        Assert.Equal(CellNotes.For("  FX"), NoteOf(Cell(grid, 0, 2)));
        Assert.All([Cell(grid, 0, 1), Cell(grid, 1, 0), Cell(grid, 1, 1), Cell(grid, 1, 2)], cell => Assert.Null(NoteOf(cell)));
    });

    [Fact]
    public void The_notes_follow_the_preview_when_it_is_rendered_again_or_replaced() => OnStaThread(() =>
    {
        var grid = new DataGrid();
        PreviewNotes.SetPreview(grid, Sample);
        PreviewGrid.SetTable(grid, Sample.Render(CultureInfo.InvariantCulture));
        using var host = Shown(grid);

        PreviewGrid.SetTable(grid, Sample.Render(CultureInfo.GetCultureInfo("de-DE")));
        Settle(grid);
        Assert.Equal(CellNotes.For("T-1\u200B"), NoteOf(Cell(grid, 0, 0)));

        SourcePreview clean = new(["Ref"], [["T-1"], [" T-2"]], 2);
        PreviewNotes.SetPreview(grid, clean);
        PreviewGrid.SetTable(grid, clean.Render(CultureInfo.InvariantCulture));
        Settle(grid);
        Assert.Null(NoteOf(Cell(grid, 0, 0)));
        Assert.Equal(CellNotes.For(" T-2"), NoteOf(Cell(grid, 1, 0)));
    });

    private static string? NoteOf(DataGridCell cell)
    {
        var help = AutomationProperties.GetHelpText(cell);
        var tip = cell.ToolTip is ToolTip { Content: TextBlock { Text: var text } } ? text : null;
        var highlighted = cell.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue;
        Assert.Equal(tip is not null, highlighted);
        Assert.Equal(tip ?? string.Empty, help);
        return tip;
    }

    private static DataGridCell Cell(DataGrid grid, int row, int column)
    {
        var container = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(row));
        return Assert.IsType<DataGridCell>(grid.Columns[column].GetCellContent(container)?.Parent);
    }

    private static HwndSource Shown(DataGrid grid)
    {
        var host = new HwndSource(new HwndSourceParameters(nameof(PreviewNotesTests)) { Width = 640, Height = 320, WindowStyle = 0 }) { RootVisual = grid };
        Settle(grid);
        return host;
    }

    private static void Settle(DataGrid grid)
    {
        grid.UpdateLayout();
        grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void OnStaThread(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
