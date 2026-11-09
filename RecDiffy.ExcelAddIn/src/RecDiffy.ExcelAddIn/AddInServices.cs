using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn;

internal static class AddInServices
{
    private static readonly ExcelDnaSources Excel = new();

    public static WindowHost Window { get; } = new(CreateShell);

    private static ShellWindow CreateShell()
    {
        var window = new ShellWindow();
        window.DataContext = new ShellViewModel(new SourcesViewModel(Excel, new WorkbookDialog(window)));
        return window;
    }
}
