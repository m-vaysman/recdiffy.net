using System.Windows;
using Microsoft.Win32;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Views;

internal sealed class WorkbookDialog(Window owner) : IWorkbookDialog
{
    public string? ChooseWorkbook(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Excel workbooks (*.xlsx;*.xlsm;*.xlsb;*.xls)|*.xlsx;*.xlsm;*.xlsb;*.xls",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) is true ? dialog.FileName : null;
    }
}
