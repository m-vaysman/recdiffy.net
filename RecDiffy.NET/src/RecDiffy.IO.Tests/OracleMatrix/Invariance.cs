using System.Globalization;
using RecDiffy.Results;

using SheetRow = System.Collections.Generic.IReadOnlyDictionary<string, object?>;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Spec v3 §10.1, AC-6 and AC-7: the same rows in another order, or with their IDs renamed by a bijection, give the same
// result. A fingerprint describes a result by row keys mapped back to the originals, never by GroupId, which changes
// under a scramble by design.
internal static class Invariance
{
    public static IReadOnlyList<SheetRow> Shuffle(IReadOnlyList<SheetRow> rows, int seed)
    {
        var random = new Random(seed);
        var order = rows.Select(_ => random.Next()).ToArray();
        return [.. rows.Zip(order).OrderBy(pair => pair.Second).Select(pair => pair.First)];
    }

    // A bijection over the given values: each becomes prefix + a position in a seeded permutation.
    public static IReadOnlyDictionary<string, string> Bijection(IEnumerable<string> values, string prefix, int seed)
    {
        var random = new Random(seed);
        var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var order = distinct.Select(_ => random.Next()).ToArray();
        return distinct.Zip(order)
            .OrderBy(pair => pair.Second)
            .Select((pair, position) => (Value: pair.First, Name: $"{prefix}{position.ToString("D4", CultureInfo.InvariantCulture)}"))
            .ToDictionary(pair => pair.Value, pair => pair.Name, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> Values(IEnumerable<SheetRow> rows, IEnumerable<string> columns) =>
        [.. rows.SelectMany(row => columns.Select(column => SheetRows.Text(row.GetValueOrDefault(column)))).OfType<string>()];

    // Renames every value of the columns that the map holds; other values, and other columns, are kept.
    public static IReadOnlyList<SheetRow> Rename(IReadOnlyList<SheetRow> rows, IReadOnlyCollection<string> columns, IReadOnlyDictionary<string, string> map) =>
        [.. rows.Select(row => (SheetRow)row.ToDictionary(
            cell => cell.Key,
            cell => columns.Contains(cell.Key, StringComparer.Ordinal) && SheetRows.Text(cell.Value) is { } text && map.TryGetValue(text, out var renamed) ? renamed : cell.Value,
            StringComparer.Ordinal))];

    public static IReadOnlyList<string> Fingerprint(ReconciliationResult result, IReadOnlyDictionary<string, string> leftBack, IReadOnlyDictionary<string, string> rightBack)
    {
        string Original(SourceRef row) => (row.Source == "left" ? leftBack : rightBack).GetValueOrDefault(row.RowKey) ?? row.RowKey;
        var entries = result.Entries.Select(entry => string.Join(
            "|",
            "entry",
            entry.Alignment,
            entry.Composition,
            entry.Outcome,
            string.Join(",", entry.Left.Select(Original).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Right.Select(Original).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Diffs.Select(Diff).Order(StringComparer.Ordinal))));
        var rows = result.Rows.Select(row => string.Join(
            "|",
            $"{row.Row.Source}:{Original(row.Row)}",
            row.Disposition,
            row.Suppression,
            row.Validity,
            string.Join(",", row.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Field}").Order(StringComparer.Ordinal))));
        return [.. entries.Concat(rows).Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> Difference(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
    [
        .. expected.Except(actual, StringComparer.Ordinal).Select(line => $"- {line}"),
        .. actual.Except(expected, StringComparer.Ordinal).Select(line => $"+ {line}"),
    ];

    private static string Diff(FieldDiff diff) =>
        $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString("0.############################", CultureInfo.InvariantCulture)}:{diff.Delta.Days.ToString(CultureInfo.InvariantCulture)}:{(diff.AttributedTo.IsDefaultOrEmpty ? "" : string.Join("+", diff.AttributedTo))}";
}
