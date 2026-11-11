using ExcelDna.Integration;

namespace RecDiffy.ExcelAddIn;

public static class LogCommands
{
    [ExcelCommand(Name = "RecDiffy.ShowLog", MenuName = "RecDiffy", MenuText = "Show log", Description = "Shows what the RecDiffy add-in did in this Excel session.")]
    public static void ShowLog() => AddInLogs.ShowWindow();
}
