using ExcelDna.Integration;

namespace RecDiffy.ExcelAddIn;

public sealed class AddInLogging : IExcelAddIn
{
    public void AutoOpen() => AddInLogs.Start();

    public void AutoClose() => AddInLogs.Stop();
}
