using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Samples;

internal static class SampleReport
{
    public static void Heading(string title) => Console.WriteLine($"{Environment.NewLine}{title}");

    public static void Result(ReconciliationResult result)
    {
        var summary = result.Summary;
        Console.WriteLine($"summary matched={summary.Matched.ToInvariant()} exact={summary.Exact.ToInvariant()} withinTolerance={summary.WithinTolerance.ToInvariant()} break={summary.Break.ToInvariant()} leftOnly={summary.LeftOnly.ToInvariant()} rightOnly={summary.RightOnly.ToInvariant()} ambiguous={summary.Ambiguous.ToInvariant()} searchIncomplete={summary.SearchIncomplete.ToInvariant()} quarantined={summary.Quarantined.ToInvariant()} suppressed={summary.Suppressed.ToInvariant()}");
        foreach (var entry in result.Entries)
            Console.WriteLine($"entry {entry.Id} path={entry.PathId} alignment={entry.Alignment} composition={entry.Composition} outcome={entry.Outcome} left=[{Keys(entry.Left)}] right=[{Keys(entry.Right)}]");
        foreach (var row in result.Rows)
            Console.WriteLine($"row {row.Row.Source}:{row.Row.RowKey} disposition={row.Disposition} validity={row.Validity}{Diagnostics(row)}");
    }

    public static void SideBySide(SideBySideView view)
    {
        foreach (var line in view.Lines)
        {
            foreach (var (measure, cell) in line.Cells)
                Console.WriteLine($"side-by-side {line.Row}/{line.Column} {measure} left={Money(cell.Left)} right={Money(cell.Right)} delta={Money(cell.Delta)} leftOnly={Money(cell.LeftOnly)} rightOnly={Money(cell.RightOnly)} matchedDelta={Money(cell.MatchedDelta)}");
        }
    }

    private static string Keys(IReadOnlyList<SourceRef> rows) => string.Join(",", rows.Select(row => row.RowKey));

    private static string Diagnostics(RowResult row) => row.Diagnostics switch
    {
        [] => "",
        _ => " diagnostics=" + string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code)),
    };

    private static string Money(decimal number) => number.ToString(CultureInfo.InvariantCulture);
}
