using System.Globalization;
using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// Golden snapshots of Run_Info and the MO1 tab on fixture-v1: every cell's text or formula. Set RECDIFFY_UPDATE_GOLDENS=1 to rewrite them
// after an intended change, and review the diff.
public class GoldenTests
{
    private static string Goldens => Path.Combine(Paths.Repo, "RecDiffy.NET", "src", "RecDiffy.Excel.Tests", "Goldens");

    [Fact]
    public void Run_info_matches_its_golden()
    {
        var book = Book.Read(Mo1());
        Compare("fixture-v1-run-info.txt", Snapshot(book, "Run_Info"));
    }

    [Fact]
    public void Control_totals_cells_match_their_golden()
    {
        var book = Book.Read(Mo1());
        Compare("fixture-v1-mo1.txt", Snapshot(book, "MO1_ControlTotals") + Snapshot(book, "Engine_SideBySide"));
    }

    [Fact]
    public void Run_info_carries_the_engine_run_and_summary()
    {
        var info = Book.Read(Mo1())["Run_Info"];
        var values = info.Values.GroupBy(cell => Row(cell.Address)).ToDictionary(group => group.Single(cell => cell.Address.StartsWith('A')).Value ?? "", group => group.SingleOrDefault(cell => cell.Address.StartsWith('B'))?.Value, StringComparer.Ordinal);
        var run = FixtureV1.Result.Run;
        var summary = FixtureV1.Result.Summary;

        Assert.Equal(run.ProfileHash, values["Profile hash"]);
        Assert.Equal(run.EngineVersion, values["Engine version"]);
        Assert.Equal(run.ReferenceId, values["Reference data"]);
        Assert.Equal("fixture-v1", values["Profile id"]);
        Assert.Equal((FixtureV1.AsOf.DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(CultureInfo.InvariantCulture), values["As-of date"]);
        Assert.Equal(summary.Matched.ToString(CultureInfo.InvariantCulture), values["Matched"]);
        Assert.Equal(summary.Break.ToString(CultureInfo.InvariantCulture), values["Break"]);
        Assert.Equal(summary.LeftOnly.ToString(CultureInfo.InvariantCulture), values["LeftOnly"]);
        Assert.Equal(summary.Ambiguous.ToString(CultureInfo.InvariantCulture), values["Ambiguous"]);
    }

    private static string Mo1()
    {
        var outcome = Assert.Single(FixtureV1.Result.WriteAll(Paths.Scratch("golden"), FixtureV1.Options(ReportKind.ControlTotals)));
        return outcome.Path!;
    }

    private static string Snapshot(Book book, string sheet) =>
        string.Concat(book[sheet].Values
            .Where(cell => cell.Formula is not null || cell.Value is not null)
            .OrderBy(cell => Row(cell.Address))
            .ThenBy(cell => cell.Address.Length)
            .ThenBy(cell => cell.Address, StringComparer.Ordinal)
            .Select(cell => $"{sheet}!{cell.Address}\t{(cell.Formula is { } formula ? "=" + formula : cell.Value)}\n"));

    private static void Compare(string name, string actual)
    {
        var path = Path.Combine(Goldens, name);
        if (Environment.GetEnvironmentVariable("RECDIFFY_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Goldens);
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"Missing golden {path}. Run the tests once with RECDIFFY_UPDATE_GOLDENS=1.");
        var expected = File.ReadAllLines(path);
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected.Length, lines.Length);
        Assert.All(expected.Zip(lines), pair => Assert.Equal(pair.First, pair.Second));
    }

    private static int Row(string address) => int.Parse(new string([.. address.SkipWhile(char.IsLetter)]), CultureInfo.InvariantCulture);
}
