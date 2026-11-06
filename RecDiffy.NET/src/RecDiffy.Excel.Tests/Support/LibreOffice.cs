using System.Diagnostics;

namespace RecDiffy.Excel.Tests.Support;

// Recalculates workbooks headless with LibreOffice ("formula recalculation on load: always") and returns the recalculated copies.
// soffice is found on PATH, in the usual install folders, or through RECDIFFY_SOFFICE. RECDIFFY_SOFFICE_DOCKER=<image> runs it in a container instead.
internal static class LibreOffice
{
    private const string Settings = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <item oor:path="/org.openoffice.Office.Calc/Formula/Load"><prop oor:name="OOXMLRecalcMode" oor:op="fuse"><value>0</value></prop></item>
        <item oor:path="/org.openoffice.Office.Calc/Formula/Load"><prop oor:name="ODFRecalcMode" oor:op="fuse"><value>0</value></prop></item>
        </oor:items>
        """;

    private static readonly Lazy<string?> Executable = new(Find);

    public static string? DockerImage => Environment.GetEnvironmentVariable("RECDIFFY_SOFFICE_DOCKER") is { Length: > 0 } image ? image : null;

    public static bool Available => DockerImage is not null || Executable.Value is not null;

    public static string Missing =>
        "LibreOffice (soffice) is not available: install it, put soffice on PATH, set RECDIFFY_SOFFICE to its path, or set RECDIFFY_SOFFICE_DOCKER to an image that has it.";

    /// <summary>Copies the files into a work folder, recalculates them, and returns the recalculated paths in the same order.</summary>
    public static IReadOnlyList<string> Recalculate(IReadOnlyList<string> files)
    {
        var work = Paths.Scratch("recalc");
        var input = Directory.CreateDirectory(Path.Combine(work, "in")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(work, "out")).FullName;
        var profile = Directory.CreateDirectory(Path.Combine(work, "profile", "user")).FullName;
        File.WriteAllText(Path.Combine(profile, "registrymodifications.xcu"), Settings);
        var names = files.Select(file => Path.GetFileName(file)).ToArray();
        foreach (var file in files)
            File.Copy(file, Path.Combine(input, Path.GetFileName(file)));
        var start = DockerImage is { } image ? Docker(image, work, names) : Local(Executable.Value!, work, names);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("soffice did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (process.WaitForExit(TimeSpan.FromMinutes(5)) is false)
        {
            process.Kill(true);
            throw new TimeoutException("soffice timed out");
        }

        var results = names.Select(name => Path.Combine(output, name)).ToArray();
        var missing = results.Where(path => File.Exists(path) is false).ToArray();
        return missing.Length == 0
            ? results
            : throw new InvalidOperationException($"soffice produced no output for {string.Join(", ", missing.Select(Path.GetFileName))}: {stdout.Result} {stderr.Result}");
    }

    private static ProcessStartInfo Local(string soffice, string work, IReadOnlyList<string> names)
    {
        var start = Start(soffice);
        foreach (var argument in Arguments(new Uri(Path.Combine(work, "profile")).AbsoluteUri, Path.Combine(work, "out"), names.Select(name => Path.Combine(work, "in", name))))
            start.ArgumentList.Add(argument);
        return start;
    }

    private static ProcessStartInfo Docker(string image, string work, IReadOnlyList<string> names)
    {
        var start = Start("docker");
        foreach (var argument in new[] { "run", "--rm", "-v", $"{work}:/work", image, "soffice" }.Concat(Arguments("file:///work/profile", "/work/out", names.Select(name => $"/work/in/{name}"))))
            start.ArgumentList.Add(argument);
        return start;
    }

    private static ProcessStartInfo Start(string file) => new(file)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    private static IEnumerable<string> Arguments(string profileUri, string outDir, IEnumerable<string> inputs) =>
        [$"-env:UserInstallation={profileUri}", "--headless", "--norestore", "--nolockcheck", "--convert-to", "xlsx:Calc MS Excel 2007 XML", "--outdir", outDir, .. inputs];

    private static string? Find()
    {
        IEnumerable<string> candidates =
        [
            Environment.GetEnvironmentVariable("RECDIFFY_SOFFICE") ?? "",
            .. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).SelectMany(dir => new[] { Path.Combine(dir, "soffice"), Path.Combine(dir, "soffice.exe") }),
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            "/usr/bin/soffice",
            "/usr/lib/libreoffice/program/soffice",
            "/Applications/LibreOffice.app/Contents/MacOS/soffice",
        ];
        return candidates.FirstOrDefault(path => path.Length > 0 && File.Exists(path));
    }
}
