using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;

namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

internal static class ReadsExcelApplication
{
    public static object Application() => ExcelDnaUtil.Application;
}

internal static class ReadsWindowHandle
{
    public static nint Handle() => ExcelDnaUtil.WindowHandle;
}

internal static class ReleasesComObjects
{
    public static int Release(object excel) => Marshal.ReleaseComObject(excel);
}

internal static class StartsExcelByProgId
{
    public static object? Start() =>
        Type.GetTypeFromProgID("Excel.Application") is { } excel ? Activator.CreateInstance(excel) : null;
}

internal static class DrivesExcelDynamically
{
    public static object? Workbook(dynamic excel) => excel.ActiveWorkbook;
}

internal static class DrivesExcelByInvokeMember
{
    public static object? Workbook(object excel) =>
        excel.GetType().InvokeMember("ActiveWorkbook", BindingFlags.GetProperty, binder: null, excel, args: null, CultureInfo.InvariantCulture);
}

internal static class TakesExcelInterop
{
    public static object Keep(Microsoft.Office.Interop.Excel.Workbook workbook) => workbook;
}

internal static class ReadsRibbonContext
{
    public static object Context(IRibbonControl control) => control.Context;
}

[ComImport]
[Guid("6F1C2B0E-5D4A-4E7B-9C3D-2A1B0C9D8E7F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeclaredComInterface;

[ComVisible(true)]
internal sealed class ExposedToCom;

[ComVisible(false)]
internal sealed class HiddenFromCom;

internal static class CallsOle32
{
    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CoInitialize(nint reserved);
}

internal sealed class SecondRibbon : ExcelRibbon;

internal static class UsesTheCApiAndWpf
{
    public static object Values(ExcelReference range) => range.GetValue();

    public static object Handle() => XlCall.Excel(XlCall.xlGetHwnd);

    public static void Later(ExcelAction action) => ExcelAsyncUtil.QueueAsMacro(action);

    public static string? Choose() =>
        new Microsoft.Win32.OpenFileDialog { Title = "Choose a workbook" }.ShowDialog() is true ? "chosen" : null;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetDpiForSystem();
}
