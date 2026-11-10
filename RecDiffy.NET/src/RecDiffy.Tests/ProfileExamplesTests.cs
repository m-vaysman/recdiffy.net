using System.Globalization;
using System.Text.RegularExpressions;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// Runs every example in RecDiffy.NET/docs/PROFILE-EXAMPLES.md and checks that it prints the output the page shows under
// it, so the page cannot drift from the engine (#60).
public class ProfileExamplesTests
{
    public static TheoryData<string> ExampleIds => [.. ProfileExamplePage.Examples.Keys];

    [Theory]
    [MemberData(nameof(ExampleIds))]
    public void Each_example_prints_the_output_the_page_shows(string id)
    {
        var example = ProfileExamplePage.Examples[id];

        var printed = ProfileExamplePage.Print(example);

        Assert.True(
            string.Equals(printed, example.Expected, StringComparison.Ordinal),
            $"PROFILE-EXAMPLES.md example '{id}' printed:\n{printed}\n\nThe page shows:\n{example.Expected}");
    }

    [Fact]
    public void Every_profile_on_the_page_is_a_checked_example()
    {
        var examples = ProfileExamplePage.ExampleBlocks();

        var unmarked = ProfileExamplePage.ProfileBlocks().Where(line => examples.Contains(line) is false).ToArray();

        Assert.True(unmarked.Length == 0, $"PROFILE-EXAMPLES.md has a profile with no example marker at line {string.Join(", ", unmarked.Select(line => line + 1))}.");
    }
}

// Reads the page's examples. Each part of an example follows an HTML comment that names it:
//   <!-- example: id -->   the profile, in the next fenced block.
//   <!-- left: id -->      the left rows, in the next Markdown table. A blank cell is a missing value.
//   <!-- right: id -->     the right rows, in the next Markdown table.
//   <!-- expected: id -->  what Print gives, in the next fenced block.
// An example with no rows prints its compile errors, or the settings compile resolved. One with rows prints the run.
internal static class ProfileExamplePage
{
    private static readonly Regex Marker = new(
        @"^<!-- (?<part>example|left|right|expected): (?<id>[a-z0-9-]+) -->$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Lazy<string[]> Lines = new(() =>
        File.ReadAllText(Path.Combine(ProfileCorpus.RepoRoot(), "RecDiffy.NET", "docs", "PROFILE-EXAMPLES.md")).ReplaceLineEndings("\n").Split('\n'));

    private static readonly Lazy<IReadOnlyDictionary<string, PageExample>> Found = new(Scan);

    public static IReadOnlyDictionary<string, PageExample> Examples => Found.Value;

    // The line of every fence that opens a block holding a whole profile.
    public static IEnumerable<int> ProfileBlocks() =>
        Fences().Where(fence => Body(Lines.Value, fence).Contains("\"formatVersion\"", StringComparison.Ordinal));

    // The line of the fence each example marker points at.
    public static IReadOnlySet<int> ExampleBlocks() =>
        Parts().Where(part => part.Kind is "example").Select(part => NextFence(Lines.Value, part.Line)).ToHashSet();

    public static string Print(PageExample example) =>
        (Compile(example.Profile), example.Left, example.Right) switch
        {
            ({ Errors: [_, ..] errors }, _, _) => string.Join('\n', errors.Select(error => $"{error.Code}: {error.Message}")),
            ({ Profile: { } profile }, { } left, { } right) => PrintRun(profile, Reconciler.Run(profile, Source("left", left), Source("right", right), ReferenceDataSet.Empty)),
            ({ Profile: { } profile }, _, _) => PrintSettings(profile),
            _ => throw new InvalidOperationException("Compile gave neither a profile nor errors."),
        };

    // The server's culture is pinned to invariant, so an example that declares no culture prints the same on every machine.
    private static (CompiledProfile? Profile, IReadOnlyList<ProfileError> Errors) Compile(string json)
    {
        var server = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return (Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()), []);
        }
        catch (ProfileCompileException exception)
        {
            return (null, exception.Errors);
        }
        finally
        {
            CultureInfo.CurrentCulture = server;
        }
    }

    private static RecordSource Source(string side, PageTable table)
    {
        var map = table.Headers
            .Select((header, index) => (Header: header, Index: index))
            .Aggregate(new SourceMap<IReadOnlyList<string>>(), (current, column) => current.Text(column.Header, row => row[column.Index] is "" ? null : row[column.Index]));
        return RecordSources.FromObjects(side, table.Rows, map);
    }

    private static string PrintSettings(CompiledProfile profile)
    {
        var document = profile.Document;
        var limits = document.Limits;
        string[] header =
        [
            $"rounding.midpoint: {document.Midpoint}",
            $"limits: maxGroupSize {Text(limits.MaxGroupSize)}, maxBlockItems {Text(limits.MaxBlockItems)}, maxTargetsPerComponent {Text(limits.MaxTargetsPerComponent)}, maxNodesPerTarget {Text(limits.MaxNodesPerTarget)}",
        ];
        var sources = new[] { "left", "right" }.Select(side => $"sources.{side}: rowKey {RowKey(document, side)}; {Reading(profile.Reading[side])}");
        var fields = document.Fields.Select(field => $"field {field.Name}: {field.Type}, {field.Role}, scale {Text(field.Scale)}");
        var paths = document.Paths.Select(path =>
            $"path {path.Id} compares: {string.Join(", ", DiffPlan.From(path, document.Fields, document.Midpoint, profile.Attributions).Steps.Select(step => $"{step.Field.Name} {step.Tolerance?.Kind ?? "Exact"}"))}");
        return string.Join('\n', [.. header, .. sources, .. fields, .. paths]);
    }

    private static string RowKey(ProfileDocument document, string side) =>
        document.SourceKeys.TryGetValue(side, out var key) && key.Length > 0 ? key : "(position)";

    private static string Reading(SideReading reading) =>
        $"culture {Quoted(reading.Culture)} from {reading.Origin}, decimal {Quoted(reading.DecimalSeparator)}, group {Quoted(reading.GroupSeparator)}; strictDateFormats {(reading.StrictDateFormats ? "true" : "false")}";

    // One line per entry in result order, its non-exact fields under it, then every row that reports a diagnostic or
    // was suppressed. Columns are padded to the widest value in the example.
    private static string PrintRun(CompiledProfile profile, ReconciliationResult result)
    {
        var paths = profile.Document.Paths.Count > 1;
        var cells = result.Entries.Select(entry => EntryCells(entry, paths)).ToArray();
        var padded = cells.Length == 0 ? 0 : cells[0].Length - 1;
        var widths = Enumerable.Range(0, padded).Select(column => cells.Max(row => row[column].Length)).ToArray();
        var entries = result.Entries.Zip(cells).SelectMany(pair => (IEnumerable<string>)
        [
            string.Join(' ', pair.Second.Select((cell, column) => column < widths.Length ? cell.PadRight(widths[column]) : cell)),
            .. pair.First.Diffs.Select(diff => $"  {diff.Field} {diff.Outcome}: {Value(diff.Left)} vs {Value(diff.Right)}"),
        ]);
        var rows = result.Rows
            .Where(row => row.Diagnostics.Count > 0 || row.Suppression is not null)
            .Select(row => $"{row.Row.Source} row {Text(row.Row.Ordinal + 1)} ({KeyOf(row.Row, "no key")}): {string.Join("; ", Facts(row))}");
        return string.Join('\n', [.. entries, .. rows]);
    }

    private static string[] EntryCells(ResultEntry entry, bool paths)
    {
        var detail = entry.Explanation.Trail.Select(attempt => attempt.Detail).OfType<string>().Select(code => $" ({code})");
        string[] cells =
        [
            entry.Alignment.ToString(),
            entry.Composition.ToString(),
            entry.Outcome.ToString(),
            $"{Keys(entry.Left)} | {Keys(entry.Right)}{string.Concat(detail)}",
        ];
        return paths ? [entry.PathId is "" ? "-" : entry.PathId, .. cells] : cells;
    }

    private static IEnumerable<string> Facts(RowResult row) =>
    [
        row.Suppression is { } suppression ? $"{row.Disposition} ({suppression})" : row.Disposition.ToString(),
        .. row.Diagnostics.Select(diagnostic =>
            $"{diagnostic.Code} {diagnostic.Severity}{(diagnostic.Field is { } field ? $" on {field}" : "")}{(diagnostic.RawText is { } raw ? $" \"{raw}\"" : "")}"),
    ];

    private static string Keys(IReadOnlyList<SourceRef> rows) =>
        rows.Count == 0 ? "-" : string.Join(" + ", rows.OrderBy(row => row.Ordinal).Select(row => KeyOf(row, $"row {Text(row.Ordinal + 1)}")));

    private static string KeyOf(SourceRef row, string blank) => row.RowKey is "" ? blank : row.RowKey;

    private static string Value(CanonicalValue value) => value switch
    {
        { IsNull: true } => "(blank)",
        { Text: { } text } => text,
        { Type: FieldType.Decimal } => value.Number.ToString(CultureInfo.InvariantCulture),
        { Type: FieldType.Date } => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, PageExample> Scan() =>
        Parts()
            .GroupBy(part => part.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Assemble(group.Key, [.. group]), StringComparer.Ordinal);

    private static IEnumerable<Part> Parts() =>
        Lines.Value
            .Select((line, index) => (Match: Marker.Match(line), Index: index))
            .Where(pair => pair.Match.Success)
            .Select(pair => new Part(pair.Match.Groups["part"].Value, pair.Match.Groups["id"].Value, pair.Index));

    private static PageExample Assemble(string id, Part[] parts)
    {
        var lines = Lines.Value;
        var example = One(parts, "example", id) ?? throw new InvalidOperationException($"PROFILE-EXAMPLES.md example '{id}' has no <!-- example: {id} --> marker.");
        var expected = One(parts, "expected", id) ?? throw new InvalidOperationException($"PROFILE-EXAMPLES.md example '{id}' has no <!-- expected: {id} --> marker.");
        return new PageExample(
            Body(lines, NextFence(lines, example.Line)),
            One(parts, "left", id) is { } left ? Table(lines, left.Line) : null,
            One(parts, "right", id) is { } right ? Table(lines, right.Line) : null,
            Body(lines, NextFence(lines, expected.Line)));
    }

    private static Part? One(Part[] parts, string kind, string id) =>
        parts.Where(part => part.Kind == kind).ToArray() switch
        {
            [] => null,
            [var part] => part,
            _ => throw new InvalidOperationException($"PROFILE-EXAMPLES.md example '{id}' has more than one {kind} marker."),
        };

    private static IEnumerable<int> Fences()
    {
        var lines = Lines.Value;
        var opening = true;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("```", StringComparison.Ordinal) is false)
                continue;
            if (opening)
                yield return index;
            opening = !opening;
        }
    }

    private static int NextFence(string[] lines, int after) =>
        Array.FindIndex(lines, after + 1, line => line.StartsWith("```", StringComparison.Ordinal)) switch
        {
            -1 => throw new InvalidOperationException($"PROFILE-EXAMPLES.md has no fenced block after line {Text(after + 1)}."),
            var open => open,
        };

    private static string Body(string[] lines, int open) =>
        string.Join('\n', lines[(open + 1)..Array.FindIndex(lines, open + 1, line => line.StartsWith("```", StringComparison.Ordinal))]);

    // The first row is the header and the second the separator.
    private static PageTable Table(string[] lines, int after)
    {
        var first = Array.FindIndex(lines, after + 1, line => line.StartsWith('|'));
        var rows = lines.Skip(first).TakeWhile(line => line.StartsWith('|')).Select(Cells).ToArray();
        return new PageTable(rows[0], rows[2..]);
    }

    private static IReadOnlyList<string> Cells(string line) =>
        [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

    private sealed record Part(string Kind, string Id, int Line);
}

internal sealed record PageExample(string Profile, PageTable? Left, PageTable? Right, string Expected);

internal sealed record PageTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
