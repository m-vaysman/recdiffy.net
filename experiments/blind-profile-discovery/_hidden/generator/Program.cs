using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecDiffy;
using RecDiffy.IO;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace BlindDiscovery.Generator;

internal static class Program
{
    public static int Main()
    {
        var root = Root();
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var pairs = Pairs.All();
        foreach (var pair in pairs)
        {
            var directory = Path.Combine(root, "pairs", pair.Id);
            Csv.Write(Path.Combine(directory, "left.csv"), pair.LeftHeader, pair.LeftRows);
            Csv.Write(Path.Combine(directory, "right.csv"), pair.RightHeader, pair.RightRows);
            var summary = Execute(pair.Key["profile"]!.ToJsonString(), Path.Combine(directory, "left.csv"), Path.Combine(directory, "right.csv"));
            pair.AssertRun(summary);
            pair.Key["leftFile"] = Json.S($"pairs/{pair.Id}/left.csv");
            pair.Key["rightFile"] = Json.S($"pairs/{pair.Id}/right.csv");
            pair.Key["leftRows"] = Json.N(pair.LeftRows.Count);
            pair.Key["rightRows"] = Json.N(pair.RightRows.Count);
            pair.Key["leftHeader"] = Json.Strings(pair.LeftHeader);
            pair.Key["rightHeader"] = Json.Strings(pair.RightHeader);
            pair.Key["verifiedRun"] = Summary(summary);
            var keyPath = Path.Combine(root, "_hidden", "keys", pair.Id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            File.WriteAllText(keyPath, pair.Key.ToJsonString(options) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine(
                $"{pair.Id} rows {pair.LeftRows.Count.ToInvariant()}/{pair.RightRows.Count.ToInvariant()}"
                + $" matched {summary.Matched.ToInvariant()} exact {summary.Exact.ToInvariant()}"
                + $" break {summary.Break.ToInvariant()} leftOnly {summary.LeftOnly.ToInvariant()}"
                + $" rightOnly {summary.RightOnly.ToInvariant()} quarantine {summary.Quarantined.ToInvariant()}");
        }

        Console.WriteLine($"wrote {pairs.Count.ToInvariant()} pairs under {root}");
        return 0;
    }

    private static RunSummary Execute(string profileJson, string left, string right)
    {
        var compiled = Reconciler.Compile(
            ProfileDocument.Parse(profileJson),
            TradingRulePack.AddTo(RuleRegistry.CreateCore()));
        var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };
        return Reconciler.Run(
            compiled,
            FileSources.FromCsv("left", left, csv),
            FileSources.FromCsv("right", right, csv),
            ReferenceDataSet.Empty).Summary;
    }

    private static JsonObject Summary(RunSummary summary) => Json.O(
        ("matched", Json.N(summary.Matched)),
        ("exact", Json.N(summary.Exact)),
        ("withinTolerance", Json.N(summary.WithinTolerance)),
        ("break", Json.N(summary.Break)),
        ("leftOnly", Json.N(summary.LeftOnly)),
        ("rightOnly", Json.N(summary.RightOnly)),
        ("ambiguous", Json.N(summary.Ambiguous)),
        ("searchIncomplete", Json.N(summary.SearchIncomplete)),
        ("quarantined", Json.N(summary.Quarantined)),
        ("suppressed", Json.N(summary.Suppressed)));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "harness", "BlindDiscovery.Harness.csproj"))
                && Directory.Exists(Path.Combine(dir.FullName, "_hidden")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the blind-profile-discovery directory.");
    }
}

internal static class Csv
{
    public static void Write(string path, string[] header, IReadOnlyList<string[]> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new string[rows.Count + 1];
        lines[0] = string.Join(',', header.Select(Cell));
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Length != header.Length)
                throw new InvalidOperationException($"{path} row {index.ToInvariant()} has {rows[index].Length.ToInvariant()} cells.");
            lines[index + 1] = string.Join(',', rows[index].Select(Cell));
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Cell(string value) =>
        value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}

internal static class Json
{
    public static JsonNode S(string value) => JsonValue.Create(value)!;

    public static JsonNode N(int value) => JsonValue.Create(value)!;

    public static JsonNode B(bool value) => JsonValue.Create(value)!;

    public static JsonObject O(params (string Name, JsonNode? Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj[name] = value;
        return obj;
    }

    public static JsonArray Strings(params string[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(JsonValue.Create(item));
        return array;
    }

    public static JsonArray List(params JsonNode[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(item);
        return array;
    }
}

internal static class Data
{
    public static readonly string[] Accounts = [.. Enumerable.Range(1, 12).Select(n => "ACC-" + n.ToString("00", CultureInfo.InvariantCulture))];

    public static readonly string[] Tickers = ["AAPL", "MSFT", "NVDA", "AMZN", "GOOG", "META", "JPM", "XOM", "TSLA", "LLY", "AVGO", "UNH", "V", "MA", "BAC"];

    public static readonly string[] Currencies = ["USD", "EUR", "GBP", "JPY", "CHF"];

    public static string Day(int offset) =>
        new DateOnly(2026, 1, 5).AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Id(string prefix, int number, int width) =>
        prefix + number.ToString(new string('0', width), CultureInfo.InvariantCulture);

    public static string Fixed(decimal value, int scale) =>
        value.ToString(scale == 0 ? "0" : "0." + new string('0', scale), CultureInfo.InvariantCulture);

    public static string Plain(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}

internal static class Check
{
    public static decimal Dec(string text) =>
        decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static void OnlyColumn(string pair, IReadOnlyList<string[]> rows, int key)
    {
        if (Distinct(rows, key) != rows.Count)
            throw new InvalidOperationException($"{pair}: column {key.ToInvariant()} is not unique.");
        for (var column = 0; column < rows[0].Length; column++)
        {
            if (column != key && Distinct(rows, column) == rows.Count)
                throw new InvalidOperationException($"{pair}: column {column.ToInvariant()} is unexpectedly unique.");
        }
    }

    public static void Composite(string pair, IReadOnlyList<string[]> rows, params int[] key)
    {
        var width = rows[0].Length;
        for (var mask = 1; mask < (1 << width); mask++)
        {
            int[] columns = [.. Enumerable.Range(0, width).Where(column => (mask & (1 << column)) != 0)];
            var unique = Distinct(rows, columns) == rows.Count;
            var covers = key.All(column => columns.Contains(column));
            if (unique != covers)
                throw new InvalidOperationException($"{pair}: columns {string.Join(',', columns)} unique={unique} covers={covers}.");
        }
    }

    public static void Same(string pair, IReadOnlyList<string[]> left, IReadOnlyList<string[]> right)
    {
        if (left.Count != right.Count)
            throw new InvalidOperationException($"{pair}: row counts differ.");
        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].SequenceEqual(right[index], StringComparer.Ordinal))
                throw new InvalidOperationException($"{pair}: row {index.ToInvariant()} differs.");
        }
    }

    private static int Distinct(IReadOnlyList<string[]> rows, params int[] columns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            seen.Add(string.Join('\u001f', columns.Select(column => row[column])));
        return seen.Count;
    }
}

internal static class Expect
{
    public static void Clean(RunSummary summary, int entries, string pair)
    {
        if (summary is { Break: 0, WithinTolerance: 0, LeftOnly: 0, RightOnly: 0, Ambiguous: 0, SearchIncomplete: 0, Quarantined: 0, Suppressed: 0 }
            && summary.Matched == entries
            && summary.Exact == entries)
            return;
        throw new InvalidOperationException($"{pair} was not a clean reconciliation: {summary}");
    }

    public static void Unmatched(RunSummary summary, int rows, string pair)
    {
        if (summary is { Matched: 0, Break: 0, Ambiguous: 0, SearchIncomplete: 0, Quarantined: 0 }
            && summary.LeftOnly == rows
            && summary.RightOnly == rows)
            return;
        throw new InvalidOperationException($"{pair} was not fully unmatched: {summary}");
    }

    public static void AllBreak(RunSummary summary, int rows, string pair)
    {
        if (summary is { Exact: 0, WithinTolerance: 0, LeftOnly: 0, RightOnly: 0, Ambiguous: 0, Quarantined: 0 }
            && summary.Matched == rows
            && summary.Break == rows)
            return;
        throw new InvalidOperationException($"{pair} was not an all-break match: {summary}");
    }
}
