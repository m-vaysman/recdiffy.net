using RecDiffy.IO;
using RecDiffy.Results;

namespace RecDiffy.Sessions.Tests;

internal static class Fixtures
{
    public static readonly string[] Names = ["orders", "fills", "executions", "allocations", "trades"];

    private static readonly CsvOptions Csv = new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    public static CompiledProfile Profile(string name) =>
        Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Path(name, "profile.json"))), RuleRegistry.CreateCore());

    public static IReconciliationResult Batch(string name) =>
        Reconciler.Run(
            Profile(name),
            FileSources.FromCsv("left", Path(name, "left.csv"), Csv),
            FileSources.FromCsv("right", Path(name, "right.csv"), Csv),
            ReferenceDataSet.Empty);

    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows(string name, string side)
    {
        var lines = File.ReadAllLines(Path(name, $"{side}.csv")).Where(line => line.Length > 0).ToArray();
        var header = lines[0].Split(',');
        return [.. lines.Skip(1).Select(line => Row(header, line.Split(',')))];
    }

    public static IReadOnlyList<string> Fingerprint(IReconciliationResult result) =>
        [
            result.Summary.ToString(),
            .. result.Entries.Select(Describe).Order(StringComparer.Ordinal),
            .. result.Rows.Select(Describe).Order(StringComparer.Ordinal),
        ];

    private static IReadOnlyDictionary<string, string?> Row(string[] header, string[] cells) =>
        header.Zip(cells).ToDictionary(pair => pair.First, pair => pair.Second.Length > 0 ? pair.Second : null, StringComparer.Ordinal);

    private static string Describe(ResultEntry entry) =>
        $"{entry.Alignment.ToInvariant()}|{entry.Composition.ToInvariant()}|{entry.Outcome.ToInvariant()}|{Keys(entry.Left)}|{Keys(entry.Right)}|{Diffs(entry)}";

    private static string Describe(RowResult row) =>
        $"{row.Row.Source}:{row.Row.RowKey}:{row.Row.Ordinal.ToInvariant()}:{row.Disposition.ToInvariant()}:{row.Suppression?.ToInvariant()}:{string.Join(',', row.Diagnostics.Select(diagnostic => diagnostic.Code))}";

    private static string Keys(IEnumerable<SourceRef> rows) => string.Join(',', rows.Select(row => row.RowKey).Order(StringComparer.Ordinal));

    private static string Diffs(ResultEntry entry) =>
        string.Join(';', entry.Diffs.Select(diff => $"{diff.Field.Name}={diff.Outcome.ToInvariant()}:{diff.Delta.Number.ToInvariant()}").Order(StringComparer.Ordinal));

    private static string Path(string name, string suffix)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(System.IO.Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        return System.IO.Path.Combine(root, "RecDiffy.NET", "test-data", "fix-reconciliation", $"{name}-{suffix}".Replace("-profile.json", ".profile.json", StringComparison.Ordinal));
    }
}
