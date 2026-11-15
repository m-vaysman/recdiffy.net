using System.Reflection;
using RecDiffy.ApiBaseline;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// A change to the public surface of a shipped library fails here, on net8.0 and net10.0, with the lines that changed.
// Accepting an intended change is a manual step: copy the .actual.txt file the failure names over the baseline in
// ApiBaseline and commit it. RecDiffy.IO and RecDiffy.Excel have the same test in their own test projects.
public class PublicApiBaselineTests
{
    private const string TestProject = "RecDiffy.Tests";

    [Fact]
    public void The_engine_public_api_matches_its_baseline() => AssertBaseline(typeof(Reconciler).Assembly);

    [Fact]
    public void The_trading_pack_public_api_matches_its_baseline() => AssertBaseline(typeof(TradingRulePack).Assembly);

    // The baseline here has lost the line of one member and holds an older signature of another. The comparison reports
    // the changed signature as gone from the assembly, and both members as new in it.
    [Fact]
    public void A_removed_member_and_a_changed_signature_are_both_reported()
    {
        var actual = PublicApiSurface.Describe(typeof(Reconciler).Assembly);
        var compile = Assert.Single(actual, line => line.StartsWith("RecDiffy.Reconciler :: method public static RecDiffy.CompiledProfile Compile(", StringComparison.Ordinal));
        var run = Assert.Single(actual, line => line.StartsWith("RecDiffy.Reconciler :: method public static RecDiffy.IReconciliationResult Run(", StringComparison.Ordinal));
        var older = compile.Replace("RecDiffy.RuleRegistry registry", "RecDiffy.RuleRegistry rules", StringComparison.Ordinal);
        Assert.False(string.Equals(compile, older, StringComparison.Ordinal));
        string[] baseline = [.. actual.Where(line => line != run).Select(line => line == compile ? older : line)];

        var difference = PublicApiSurface.Compare(baseline, actual);

        Assert.Equal([older], difference.Removed);
        Assert.Equal([compile, run], difference.Added);
        Assert.False(difference.IsEmpty);
        Assert.True(PublicApiSurface.Compare(actual, actual).IsEmpty);
    }

    // The failure names both files and says how to accept, writes the actual surface, and leaves the baseline as it was.
    [Fact]
    public void A_difference_fails_with_the_changed_lines_and_never_rewrites_the_baseline()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ApiBaseline", "proof");
        Directory.CreateDirectory(folder);
        var baselinePath = Path.Combine(folder, "Sample.txt");
        var actualPath = Path.Combine(folder, "Sample.actual.txt");
        string[] baseline = ["Sample.Kept :: type public sealed class", "Sample.Kept :: method public int Count(string text)", "Sample.Kept :: method public void Gone()"];
        string[] actual = ["Sample.Kept :: method public int Count(string text, bool strict)", "Sample.Kept :: type public sealed class"];
        var committed = PublicApiSurface.Render("Sample", baseline);
        File.WriteAllText(baselinePath, committed);
        File.Delete(actualPath);

        var failure = PublicApiSurface.Check("Sample", actual, baselinePath, actualPath);

        Assert.NotNull(failure);
        Assert.Contains("The public API of Sample differs from its baseline: 2 lines of the baseline are gone or changed, and 1 line is new.", failure, StringComparison.Ordinal);
        Assert.Contains("\n- Sample.Kept :: method public int Count(string text)\n", failure, StringComparison.Ordinal);
        Assert.Contains("\n- Sample.Kept :: method public void Gone()\n", failure, StringComparison.Ordinal);
        Assert.Contains("\n+ Sample.Kept :: method public int Count(string text, bool strict)\n", failure, StringComparison.Ordinal);
        Assert.Contains(baselinePath, failure, StringComparison.Ordinal);
        Assert.Contains(actualPath, failure, StringComparison.Ordinal);
        Assert.Contains("Accept it by hand", failure, StringComparison.Ordinal);
        Assert.Contains("Nothing rewrites the baseline for you.", failure, StringComparison.Ordinal);
        Assert.Equal(committed, File.ReadAllText(baselinePath));
        Assert.Equal(actual, PublicApiSurface.Read(actualPath));
        Assert.Null(PublicApiSurface.Check("Sample", baseline, baselinePath, actualPath));
    }

    [Fact]
    public void A_missing_baseline_fails_and_is_not_created()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ApiBaseline", "proof");
        Directory.CreateDirectory(folder);
        var baselinePath = Path.Combine(folder, "Absent.txt");
        var actualPath = Path.Combine(folder, "Absent.actual.txt");
        File.Delete(baselinePath);

        var failure = PublicApiSurface.Check("Absent", ["Absent.Type :: type public sealed class"], baselinePath, actualPath);

        Assert.NotNull(failure);
        Assert.Contains("has no baseline", failure, StringComparison.Ordinal);
        Assert.False(File.Exists(baselinePath));
        Assert.True(File.Exists(actualPath));
    }

    // Two members that read the same would hide the removal of one of them.
    [Fact]
    public void A_repeated_line_fails()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ApiBaseline", "proof");
        var failure = PublicApiSurface.Check("Twice", ["Twice.Type :: method public void Run()", "Twice.Type :: method public void Run()"], Path.Combine(folder, "Twice.txt"), Path.Combine(folder, "Twice.actual.txt"));

        Assert.NotNull(failure);
        Assert.Contains("repeats a line", failure, StringComparison.Ordinal);
    }

    // The surface reads what a caller compiles against: names, nullable annotations, defaults, constraints and enum values.
    [Fact]
    public void A_line_carries_the_whole_signature()
    {
        var engine = PublicApiSurface.Describe(typeof(Reconciler).Assembly);

        Assert.Contains(
            "RecDiffy.Reconciler :: method public static RecDiffy.IReconciliationResult Run(RecDiffy.CompiledProfile profile, RecDiffy.RecordSource left, RecDiffy.RecordSource right, RecDiffy.ReferenceDataSet referenceData, RecDiffy.RunOptions? options = null, System.Threading.CancellationToken cancellationToken = default)",
            engine,
            StringComparer.Ordinal);
        Assert.Contains("RecDiffy.Results.Composition :: type [Flags] public enum : int", engine, StringComparer.Ordinal);
        Assert.Contains("RecDiffy.Results.Composition :: enum value LifecycleCollapsed = 16", engine, StringComparer.Ordinal);
        Assert.Contains("RecDiffy.Reconciler :: type public static class", engine, StringComparer.Ordinal);
        Assert.DoesNotContain(engine, line => line.StartsWith("RecDiffy.RunPipeline ::", StringComparison.Ordinal));
    }

    // PublicApiSurface.cs is compiled into three test projects, one copy each. The copies must not drift apart.
    [Theory]
    [InlineData("RecDiffy.IO.Tests")]
    [InlineData("RecDiffy.Excel.Tests")]
    public void The_copy_of_the_surface_reader_is_the_same_in_every_test_project(string project)
    {
        static string Source(string project) =>
            File.ReadAllText(Path.Combine(ProfileCorpus.Source, project, "ApiBaseline", "PublicApiSurface.cs")).ReplaceLineEndings("\n");

        Assert.True(
            string.Equals(Source(TestProject), Source(project), StringComparison.Ordinal),
            $"{project}/ApiBaseline/PublicApiSurface.cs differs from the copy in {TestProject}. Copy the file again so the three are the same.");
    }

    private static void AssertBaseline(Assembly assembly)
    {
        var failure = PublicApiSurface.Check(assembly, TestProject);
        Assert.True(failure is null, failure);
    }
}
