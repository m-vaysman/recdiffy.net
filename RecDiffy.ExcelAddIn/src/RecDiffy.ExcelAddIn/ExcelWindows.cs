using System.Runtime.InteropServices;

namespace RecDiffy.ExcelAddIn;

internal static class ExcelWindows
{
    private const string ExcelWindowClass = "XLMAIN";

    public static nint Resolve(object? apiHandle) => apiHandle switch
    {
        double value => Match(ToHandle(value)),
        _ => 0,
    };

    public static nint Foremost() => TopLevelExcelWindows().FirstOrDefault(IsWindowVisible);

    public static void BringForward(nint window)
    {
        if (window is not 0)
            _ = SetForegroundWindow(window);
    }

    private static nint ToHandle(double value) => value < 0 ? (nint)(int)value : (nint)(uint)value;

    private static nint Match(nint candidate) =>
        IsExcelWindow(candidate)
            ? candidate
            : TopLevelExcelWindows().FirstOrDefault(window => LowWord(window) == LowWord(candidate));

    private static int LowWord(nint handle) => (int)(handle & 0xFFFF);

    private static bool IsExcelWindow(nint handle) =>
        handle is not 0
        && IsWindow(handle)
        && GetWindowThreadProcessId(handle, out var processId) is not 0
        && processId == (uint)Environment.ProcessId
        && string.Equals(ClassName(handle), ExcelWindowClass, StringComparison.Ordinal);

    private static List<nint> TopLevelExcelWindows()
    {
        var windows = new List<nint>();
        EnumWindows(
            (handle, _) =>
            {
                if (IsExcelWindow(handle))
                    windows.Add(handle);
                return true;
            },
            0);
        return windows;
    }

    private static string ClassName(nint handle)
    {
        var buffer = new char[256];
        var length = GetClassName(handle, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(nint handle, char[] className, int maxCount);
}
