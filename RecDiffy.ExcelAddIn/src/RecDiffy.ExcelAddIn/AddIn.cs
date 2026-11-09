using ExcelDna.Integration;

namespace RecDiffy.ExcelAddIn;

public sealed class AddIn : IExcelAddIn
{
    public void AutoOpen()
    {
    }

    public void AutoClose() => AddInServices.Window.Close();
}
