using System.Text.RegularExpressions;

namespace RecDiffy.Tests;

// #331: Mechanical source-level gates enforcing the R1, R2, R4, and R5 architecture invariants across the engine.
public class ArchitectureInvariantGateTests
{
    private static readonly Regex FloatOrDoubleToken = new(
        @"\b(?:double|float|Double|Single)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    private static readonly Regex UncheckedSumCall = new(
        @"(?<!CheckedDecimal)\.Sum\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    private static IReadOnlyList<(string RelativePath, string Content)> EngineSources()
    {
        string[] roots =
        [
            Path.Combine(ProfileCorpus.Source, "RecDiffy"),
            Path.Combine(ProfileCorpus.Source, "RecDiffy.RulePacks.Trading"),
        ];

        return
        [
            .. roots
                .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => (
                    RelativePath: Path.GetRelativePath(ProfileCorpus.Source, file).Replace('\\', '/'),
                    Content: File.ReadAllText(file))),
        ];
    }

    [Fact]
    public void R1_composite_keys_use_Delimited_Join_and_never_raw_string_Join_with_control_separators()
    {
        var sources = EngineSources();
        Assert.NotEmpty(sources);

        var rawControlJoins = sources
            .Where(file => !file.RelativePath.EndsWith("/Delimited.cs", StringComparison.Ordinal))
            .Where(file =>
                file.Content.Contains("string.Join('\\u001f'", StringComparison.Ordinal)
                || file.Content.Contains("string.Join(\"\\u001f\"", StringComparison.Ordinal)
                || file.Content.Contains("string.Join('\\u001e'", StringComparison.Ordinal)
                || file.Content.Contains("string.Join(\"\\u001e\"", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        var schemeSeparatorLeaks = sources
            .Where(file => file.RelativePath != "RecDiffy/Matching/MatchKeys.cs")
            .Where(file => file.Content.Contains("\\u001e", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(rawControlJoins);
        Assert.Empty(schemeSeparatorLeaks);
    }

    [Fact]
    public void R2_MatchKeys_KeyText_is_confined_to_MatchKeys_and_matching_consolidation_diff_results_never_use_float_or_double()
    {
        var sources = EngineSources();

        var keyTextLeaks = sources
            .Where(file => file.RelativePath != "RecDiffy/Matching/MatchKeys.cs")
            .Where(file =>
                file.Content.Contains("KeyText(", StringComparison.Ordinal)
                || file.Content.Contains("IdentifierKeyText(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        string[] decimalOnlyFolders =
        [
            "RecDiffy/Matching/",
            "RecDiffy/Consolidation/",
            "RecDiffy/Diff/",
            "RecDiffy/Results/",
        ];

        var floatViolations = sources
            .Where(file => decimalOnlyFolders.Any(prefix => file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(file => FloatOrDoubleToken.IsMatch(file.Content))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(keyTextLeaks);
        Assert.Empty(floatViolations);
    }

    [Fact]
    public void R4_Composition_flags_are_assigned_only_in_ResultBuilder()
    {
        var sources = EngineSources();

        var compositionLeaks = sources
            .Where(file =>
                file.RelativePath != "RecDiffy/Results/ResultBuilder.cs"
                && file.RelativePath != "RecDiffy/Results/ResultModel.cs")
            .Where(file => file.Content.Contains("Composition.", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(compositionLeaks);
    }

    [Fact]
    public void R5_economic_stages_use_CheckedDecimal_Sum_and_never_unchecked_Enumerable_Sum()
    {
        var sources = EngineSources();

        string[] checkedDecimalFolders =
        [
            "RecDiffy/Consolidation/",
            "RecDiffy/Diff/",
            "RecDiffy/Matching/",
            "RecDiffy/Results/SideBySide.cs",
            "RecDiffy.RulePacks.Trading/",
        ];

        var uncheckedSums = sources
            .Where(file => checkedDecimalFolders.Any(prefix => file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(file => UncheckedSumCall.IsMatch(file.Content))
            .Select(file => file.RelativePath)
            .ToArray();

        Assert.Empty(uncheckedSums);
    }
}
