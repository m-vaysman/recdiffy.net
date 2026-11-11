using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn;

internal static class AddInServices
{
    private static readonly CultureInfo ExcelCulture = CultureInfo.CurrentCulture;

    private static readonly IExcelSources Excel = new LoggedExcelSources(new ExcelDnaSources(ShowAbove), AddInLogs.Log, AddInLogs.Clock);

    private static readonly ICsvSources Csv = new LoggedCsvSources(new CsvFileSources(), AddInLogs.Log, AddInLogs.Clock);

    private static readonly IReconciliationRuns Runs = new LoggedRuns(new EngineRuns(Excel, TimeProvider.System), AddInLogs.Log, AddInLogs.Clock);

    private static readonly IResultOutput Output = new LoggedResultOutput(new ExcelResultOutput(), AddInLogs.Log, AddInLogs.Clock);

    private static readonly ISettingsStore SettingsStore = new LoggedSettingsStore(new JsonSettingsStore(JsonSettingsStore.ForWindowsUser()), AddInLogs.Log, AddInLogs.Clock);

    public static WindowHost Window { get; } = new(LoggedWindows.Wrap(CreateShell, AddInLogs.Log, LogAreas.Window, "The reconciliation window"));

    private static Task ShowAbove(nint owner) => Window.ShowAbove(owner);

    private static ShellWindow CreateShell()
    {
        var window = new ShellWindow();
        window.DataContext = new ShellViewModel(
            new SourcesViewModel(new SourceServices(Excel, Csv, new FileDialogs(window)), ExcelCulture),
            Runs,
            Output,
            new SettingsViewModel(SettingsStore));
        return window;
    }
}
