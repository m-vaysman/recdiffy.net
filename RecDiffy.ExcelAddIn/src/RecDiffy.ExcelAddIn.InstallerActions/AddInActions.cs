using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace RecDiffy.ExcelAddIn.InstallerActions;

public static class AddInActions
{
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\excel.exe";

    [CustomAction]
    public static ActionResult FindExcel(Session session)
    {
        if (session is null)
            throw new ArgumentNullException(nameof(session));

        var excel = ExcelPath();
        var bitness = excel is null ? null : BitnessOf(excel);
        session["EXCELPATH"] = excel ?? string.Empty;
        session["EXCELBITNESS"] = bitness ?? string.Empty;
        session["ADDINFILE"] = ExcelBitness.AddInFor(bitness) ?? string.Empty;
        session["EXCELRUNNING"] = ExcelIsRunning() ? "1" : string.Empty;
        session.Log($"RecDiffy: Excel at '{excel}', {bitness ?? "unknown bitness"}, running: {ExcelIsRunning()}.");
        return ActionResult.Success;
    }

    [CustomAction]
    public static ActionResult RegisterAddIn(Session session)
    {
        if (session is null)
            throw new ArgumentNullException(nameof(session));

        var addIn = session.CustomActionData["AddIn"];
        Update(session, commands => OpenValues.WithAddIn(commands, addIn));
        return ActionResult.Success;
    }

    [CustomAction]
    public static ActionResult UnregisterAddIn(Session session)
    {
        if (session is null)
            throw new ArgumentNullException(nameof(session));

        var folder = session.CustomActionData["Folder"];
        Update(session, commands => OpenValues.WithoutFolder(commands, folder));
        return ActionResult.Success;
    }

    private static void Update(Session session, Func<IReadOnlyList<string>, IReadOnlyList<string>> change)
    {
        using var options = Registry.CurrentUser.CreateSubKey(OpenValues.OptionsKey);
        var current = options.GetValueNames()
            .Where(OpenValues.IsOpenName)
            .Select(name => (Name: name, Value: options.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string))
            .Where(item => item.Value is not null)
            .ToDictionary(item => item.Name, item => item.Value!, StringComparer.OrdinalIgnoreCase);
        var changes = OpenValues.Changes(current, change(OpenValues.Ordered(current)));
        foreach (var write in changes.Writes)
        {
            options.SetValue(write.Key, write.Value, RegistryValueKind.String);
            session.Log($"RecDiffy: set {write.Key} = {write.Value}");
        }

        foreach (var name in changes.Deletes)
        {
            options.DeleteValue(name, throwOnMissingValue: false);
            session.Log($"RecDiffy: deleted {name}");
        }
    }

    private static string? ExcelPath() =>
        new[]
        {
            (Hive: RegistryHive.CurrentUser, View: RegistryView.Default),
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry64),
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry32),
        }
        .Select(place => AppPath(place.Hive, place.View))
        .FirstOrDefault(path => path is not null && File.Exists(path));

    private static string? AppPath(RegistryHive hive, RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(AppPaths);
        return key?.GetValue(null) is string path ? path.Trim('"') : null;
    }

    private static string? BitnessOf(string excel)
    {
        using var file = new FileStream(excel, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ExcelBitness.Of(ExcelBitness.MachineType(file));
    }

    private static bool ExcelIsRunning()
    {
        var processes = Process.GetProcessesByName("EXCEL");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
