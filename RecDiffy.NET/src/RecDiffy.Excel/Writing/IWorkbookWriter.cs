using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Writing;

internal interface IWorkbookWriter
{
    void Write(Workbook workbook, Stream output);
}
