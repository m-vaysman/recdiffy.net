using System.Globalization;

namespace RecDiffy.Capacity;

internal sealed class CapacityUsageException(string message) : Exception(message);

internal enum Sweep
{
    Sizes,
    Concurrency,
}

internal sealed record CapacityOptions(
    string? Out,
    IReadOnlyList<int> Sizes,
    int Seed,
    int Repeats,
    double BreakRate,
    double UnmatchedRate,
    bool WriteExcel,
    int CaseTimeoutSeconds,
    string? Commit,
    string? Ref,
    Sweep Sweep,
    IReadOnlyList<int> Levels,
    int ConcurrencyRows)
{
    public const int DefaultSeed = 377;
    public const int DefaultConcurrencyRows = 10_000;

    private static readonly string[] Known =
    [
        "--out",
        "--sizes",
        "--seed",
        "--repeats",
        "--break-rate",
        "--unmatched-rate",
        "--excel",
        "--case-timeout-seconds",
        "--commit",
        "--ref",
        "--sweep",
        "--levels",
        "--concurrency-rows",
    ];

    public static IReadOnlyList<int> DefaultSizes { get; } = [10_000, 100_000];

    public static bool IsHelpRequest(IReadOnlyList<string> args) => args is ["--help" or "-h"];

    public static CapacityOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = ReadPairs(args);
        var unknown = values.Keys.FirstOrDefault(key => !Known.Contains(key, StringComparer.Ordinal));
        if (unknown is not null)
            throw new CapacityUsageException($"Unknown option '{unknown}'.{Environment.NewLine}{Usage}");

        return new CapacityOptions(
            values.TryGetValue("--out", out var folder) ? folder : null,
            values.TryGetValue("--sizes", out var sizes) ? ParseSizes(sizes) : DefaultSizes,
            values.TryGetValue("--seed", out var seed) ? ParseSeed(seed) : DefaultSeed,
            values.TryGetValue("--repeats", out var repeats) ? ParseRepeats(repeats) : 1,
            values.TryGetValue("--break-rate", out var breaks) ? ParseRate(breaks, "--break-rate") : 0.10,
            values.TryGetValue("--unmatched-rate", out var unmatched) ? ParseRate(unmatched, "--unmatched-rate") : 0.05,
            values.TryGetValue("--excel", out var excel) ? ParseExcel(excel) : true,
            values.TryGetValue("--case-timeout-seconds", out var timeout) ? ParseTimeout(timeout) : 0,
            values.TryGetValue("--commit", out var commit) ? commit : null,
            values.TryGetValue("--ref", out var reference) ? reference : null,
            values.TryGetValue("--sweep", out var sweep) ? ParseSweep(sweep) : Sweep.Sizes,
            values.TryGetValue("--levels", out var levels) ? ParseLevels(levels) : [],
            values.TryGetValue("--concurrency-rows", out var concurrencyRows) ? ParseRows(concurrencyRows) : DefaultConcurrencyRows);
    }

    public static string Usage => """
        RecDiffy capacity harness (issue #377, phases 1 and 3).

        Usage: RecDiffy.Capacity [options]

          --out <folder>                 Run folder. Default: ./capacity-runs/<run-id>.
          --sweep <sizes|concurrency>    What to measure. Default: sizes.
          --sizes <a,b>                  Sizes sweep: rows per side, ascending. Default: 10000,100000.
          --levels <a,b|auto>            Concurrency sweep: parallel runs. Default: auto (1,2,4,8,cores,2x cores).
          --concurrency-rows <n>         Concurrency sweep: rows per side in each run. Default: 10000.
          --seed <n>                     Dataset seed. Default: 377.
          --repeats <n>                  Measured runs per size, or batches per concurrency level. Default: 1.
          --break-rate <0..1>            Share of matched pairs built to break. Default: 0.1.
          --unmatched-rate <0..1>        Share of rows built single-sided. Default: 0.05.
          --excel <one|none>             Sizes sweep: write the bounded Excel report. Default: one.
          --case-timeout-seconds <n>     Sizes sweep: cancel a case after n seconds. Default: 0 (no timeout).
          --commit <sha>                 Record this commit instead of asking git.
          --ref <name>                   Record this ref instead of asking git.
          --help                         Print this text.
        """;

    private static Dictionary<string, string> ReadPairs(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            if (name is not { Length: > 1 } || name[0] != '-')
                throw new CapacityUsageException($"Unexpected argument '{name}'.{Environment.NewLine}{Usage}");
            if (index + 1 >= args.Count)
                throw new CapacityUsageException($"'{name}' needs a value.{Environment.NewLine}{Usage}");
            if (!values.TryAdd(name, args[index + 1]))
                throw new CapacityUsageException($"'{name}' is given more than once.{Environment.NewLine}{Usage}");
        }

        return values;
    }

    private static IReadOnlyList<int> ParseSizes(string text)
    {
        int[] sizes =
        [
            .. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture))
                .Distinct()
                .Order(),
        ];
        if (sizes.Length is 0)
            throw new CapacityUsageException("--sizes needs at least one row count.");
        if (sizes.Any(size => size < 2))
            throw new CapacityUsageException("--sizes must be 2 or more rows per side.");
        return sizes;
    }

    private static int ParseSeed(string text) =>
        int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int ParseRepeats(string text)
    {
        var repeats = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return repeats is >= 1 and <= 20
            ? repeats
            : throw new CapacityUsageException("--repeats must be from 1 to 20.");
    }

    private static double ParseRate(string text, string name)
    {
        var rate = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return rate is >= 0 and <= 1
            ? rate
            : throw new CapacityUsageException($"'{name}' must be from 0 to 1.");
    }

    private static int ParseTimeout(string text)
    {
        var seconds = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return seconds >= 0
            ? seconds
            : throw new CapacityUsageException("--case-timeout-seconds must be 0 or more.");
    }

    private static bool ParseExcel(string text) => text switch
    {
        "one" => true,
        "none" => false,
        _ => throw new CapacityUsageException("--excel must be 'one' or 'none'."),
    };

    private static Sweep ParseSweep(string text) => text switch
    {
        "sizes" => Sweep.Sizes,
        "concurrency" => Sweep.Concurrency,
        _ => throw new CapacityUsageException("--sweep must be 'sizes' or 'concurrency'."),
    };

    private static IReadOnlyList<int> ParseLevels(string text)
    {
        if (text is "auto")
            return [];
        int[] levels =
        [
            .. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture))
                .Distinct()
                .Order(),
        ];
        if (levels.Length is 0)
            throw new CapacityUsageException("--levels needs at least one parallel run count, or 'auto'.");
        if (levels.Any(level => level is < 1 or > 256))
            throw new CapacityUsageException("--levels must be from 1 to 256 parallel runs.");
        return levels;
    }

    private static int ParseRows(string text)
    {
        var rows = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return rows >= 2
            ? rows
            : throw new CapacityUsageException("--concurrency-rows must be 2 or more rows per side.");
    }
}
