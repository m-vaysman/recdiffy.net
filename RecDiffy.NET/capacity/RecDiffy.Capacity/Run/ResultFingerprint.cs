using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecDiffy.Results;

namespace RecDiffy.Capacity;

// A canonical text of everything a caller can read from a result: the summary, every entry with its diffs and both
// projections, and every row with its disposition and diagnostics. Concurrent runs must reproduce the single-threaded
// text exactly, or the sweep fails.
internal static class ResultFingerprint
{
    public static string Of(ReconciliationResult result, IReadOnlyList<FieldDeclaration> fields)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fields);
        var text = new StringBuilder();
        Summary(text, result.Summary);
        foreach (var entry in result.Entries)
            Entry(text, result, entry, fields);
        foreach (var row in result.Rows)
            Row(text, row);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static void Summary(StringBuilder text, RunSummary summary) =>
        text.Append("S|")
            .Append(summary.Matched).Append(',')
            .Append(summary.Exact).Append(',')
            .Append(summary.WithinTolerance).Append(',')
            .Append(summary.Break).Append(',')
            .Append(summary.LeftOnly).Append(',')
            .Append(summary.RightOnly).Append(',')
            .Append(summary.Ambiguous).Append(',')
            .Append(summary.SearchIncomplete).Append(',')
            .Append(summary.Quarantined).Append(',')
            .Append(summary.Suppressed).Append('\n');

    private static void Entry(StringBuilder text, ReconciliationResult result, ResultEntry entry, IReadOnlyList<FieldDeclaration> fields)
    {
        text.Append("E|").Append(entry.Id.Value).Append('|').Append(entry.PathId).Append('|')
            .Append(entry.Alignment).Append('|').Append(entry.Composition).Append('|').Append(entry.Outcome).Append('|');
        Refs(text, entry.Left);
        Refs(text, entry.Right);
        foreach (var diff in entry.Diffs)
            Diff(text, diff);
        text.Append("x|").Append(entry.Explanation.CommittedBy ?? "").Append('|');
        foreach (var attempt in entry.Explanation.Trail)
            text.Append(attempt.PassId).Append('=').Append(attempt.Verdict).Append(',').Append(attempt.Key ?? "").Append(';');
        if (entry.Alignment is Alignment.Matched)
        {
            Value(text, result.Projection(entry.Id, Side.Left), fields);
            Value(text, result.Projection(entry.Id, Side.Right), fields);
        }

        text.Append('\n');
    }

    private static void Refs(StringBuilder text, IReadOnlyList<SourceRef> refs)
    {
        foreach (var reference in refs)
            text.Append(reference.Source).Append(':').Append(reference.RowKey).Append(':').Append(reference.Ordinal).Append(',');
        text.Append('|');
    }

    private static void Diff(StringBuilder text, FieldDiff diff) =>
        text.Append(diff.Field.Name).Append('=').Append(diff.Outcome).Append(':')
            .Append(diff.Delta.Kind).Append(':')
            .Append(diff.Delta.Number.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(diff.Delta.Days).Append(':')
            .Append(diff.Tolerance.Value).Append(':')
            .Append(diff.Attribution.Value).Append(':')
            .Append(string.Join(",", diff.AttributedTo)).Append(';');

    private static void Value(StringBuilder text, CanonicalRow row, IReadOnlyList<FieldDeclaration> fields)
    {
        foreach (var field in fields)
        {
            var value = row[field.Name];
            text.Append(field.Name).Append('=')
                .Append(value.Type).Append(':')
                .Append(value.IsNull ? "null" : "value").Append(':')
                .Append(value.Number.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(':')
                .Append(value.Text ?? "").Append(';');
        }

        text.Append('|');
    }

    private static void Row(StringBuilder text, RowResult row)
    {
        text.Append("R|").Append(row.Row.Source).Append(':').Append(row.Row.RowKey).Append(':').Append(row.Row.Ordinal).Append('|')
            .Append(row.Disposition).Append('|').Append(row.Suppression?.ToString() ?? "").Append('|')
            .Append(row.Validity).Append('|').Append(row.EntryId?.Value ?? "").Append('|');
        foreach (var diagnostic in row.Diagnostics)
            text.Append(diagnostic.Code).Append(':').Append(diagnostic.Severity).Append(':')
                .Append(diagnostic.Field ?? "").Append(':').Append(diagnostic.RawText ?? "").Append(';');
        text.Append('\n');
    }
}
