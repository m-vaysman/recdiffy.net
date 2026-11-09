using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn;

internal static class AddInServices
{
    private static readonly CultureInfo ExcelCulture = CultureInfo.CurrentCulture;

    private static readonly ExcelDnaSources Excel = new(ShowAbove);

    private static readonly CsvFileSources Csv = new();

    public static WindowHost Window { get; } = new(CreateShell);

    private static Task ShowAbove(nint owner) => Window.ShowAbove(owner);

    private static ShellWindow CreateShell()
    {
        var window = new ShellWindow();
        window.DataContext = new ShellViewModel(new SourcesViewModel(new SourceServices(Excel, Csv, new FileDialogs(window)), ExcelCulture));
        return window;
    }
}
