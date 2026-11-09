using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn;

internal static class AddInServices
{
    public static WindowHost Window { get; } =
        new(() => new ShellWindow { DataContext = new ShellViewModel(new SourcesViewModel()) });
}
