using System.Windows;
using Microsoft.Win32;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Views;

internal sealed class FileDialogs(Window owner) : IFileDialogs
{
    public string? ChooseWorkbook(string title) =>
        Choose(title, "Excel workbooks (*.xlsx;*.xlsm;*.xlsb;*.xls)|*.xlsx;*.xlsm;*.xlsb;*.xls");

    public string? ChooseCsvFile(string title) =>
        Choose(title, "CSV and text files (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|All files (*.*)|*.*");

    private string? Choose(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) is true ? dialog.FileName : null;
    }
}
