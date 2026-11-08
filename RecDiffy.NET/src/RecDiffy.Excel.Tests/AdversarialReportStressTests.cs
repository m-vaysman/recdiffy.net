using System.Diagnostics;
using System.Globalization;
using RecDiffy.Excel.Tests.Support;
using Xunit.Abstractions;

namespace RecDiffy.Excel.Tests;

// Adversarial pass on main. Writes every report for a large result: the synthetic dataset (lifecycle chains, a
// package, an allocation, tolerances, breaks, unmatched rows) copied many times with distinct ids, instruments, UTIs,
// package links and lifecycle targets. Every report must be written or skipped with a reason, and every written file
// must be a valid Open XML package. Scale with RECDIFFY_REPORT_COPIES (default 200).
public class AdversarialReportStressTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_report_writes_a_valid_workbook_for_a_large_result()
    {
        var copies = int.TryParse(Environment.GetEnvironmentVariable("RECDIFFY_REPORT_COPIES"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 200;
        var ours = Enumerable.Range(0, copies).SelectMany(copy => SyntheticData.Ours.Select(trade => Copy(trade, copy))).ToArray();
        var theirs = Enumerable.Range(0, copies).SelectMany(copy => SyntheticData.Theirs.Select(trade => Copy(trade, copy))).ToArray();
        var clock = Stopwatch.StartNew();
        var result = Reconciler.Run(SyntheticData.Profile, SyntheticData.Source("ours", ours), SyntheticData.Source("theirs", theirs), ReferenceDataSet.Empty);
        var ran = clock.Elapsed;
        var folder = Path.Combine(Path.GetTempPath(), "recdiffy-report-stress", Guid.NewGuid().ToString("N"));
        var outcomes = result.WriteAll(folder, SyntheticData.Options());
        var wrote = clock.Elapsed - ran;
        var problems = outcomes
            .SelectMany(outcome => outcome.Written
                ? Workbooks.SchemaErrors(outcome.Path!).Take(3).Select(error => $"{outcome.Kind}: {error}")
                : string.IsNullOrWhiteSpace(outcome.SkipReason) ? [$"{outcome.Kind}: skipped with no reason"] : [])
            .ToArray();
        output.WriteLine($"rows={result.Rows.Count} entries={result.Entries.Count} run={ran.TotalSeconds:F1}s write={wrote.TotalSeconds:F1}s validate={(clock.Elapsed - ran - wrote).TotalSeconds:F1}s");
        foreach (var outcome in outcomes)
            output.WriteLine($"{outcome.Kind}: {(outcome.Written ? $"{new FileInfo(outcome.Path!).Length / 1024} KB" : "skipped: " + outcome.SkipReason)}");
        foreach (var problem in problems.Take(20))
            output.WriteLine(problem);
        Assert.Empty(problems);
    }

    private static SyntheticData.Trade Copy(SyntheticData.Trade trade, int copy)
    {
        var suffix = "-" + copy.ToString(CultureInfo.InvariantCulture);
        string? Tag(string? text) => text is null ? null : text + suffix;
        return trade with
        {
            Id = trade.Id + suffix,
            Instrument = trade.Instrument + suffix,
            Uti = Tag(trade.Uti),
            EventTarget = Tag(trade.EventTarget),
            PackageRef = Tag(trade.PackageRef),
        };
    }
}
