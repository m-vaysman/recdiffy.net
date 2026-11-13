using System.ComponentModel;
using System.Diagnostics;

namespace RecDiffy.Capacity;

internal static class ProcessRunner
{
    public static string? Run(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(10_000) && process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
