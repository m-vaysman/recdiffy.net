using System.Text.RegularExpressions;

namespace RecDiffy.Tests;

// #331: Mechanical source-level gates enforcing the R1, R2, R4, and R5 architecture invariants across the engine.
// The detectors are internal so ArchitectureInvariantGateDetectorTests can prove each one trips on a planted
// violation: a gate no bad input can trip is a bug, not a gate.
public class ArchitectureInvariantGateTests
{
    internal static readonly Regex FloatOrDoubleToken = new(
        @"\b(?:double|float|Double|Single)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    internal static readonly Regex UncheckedSumCall = new(
        @"(?<!CheckedDecimal)\.Sum\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    // ResultBuilder.Compose sums member counts (int); its decimal sums go through CheckedDecimal.Sum. The
    // allowance is anchored on the count expression, so editing those lines stops matching and trips the gate.
    internal static readonly Regex CountSumAllowance = new(
        @"\.Sum\(row => row\.Packaged \? row\.Members\.Count : 1\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    internal static readonly string[] RawControlJoinTokens =
    [
        "string.Join('\u001f'",
        "string.Join(\"\u001f\"",
        "string.Join('\u001e'",
        "string.Join(\"\u001e\"",
    ];

    internal static readonly string[] EngineRoots =
    [
        Path.Combine(ProfileCorpus.Source, "RecDiffy"),
        Path.Combine(ProfileCorpus.Source, "RecDiffy.RulePacks.Trading"),
    ];

    // Rules/ and RecDiffy.RulePacks.Trading/ are deliberately outside the decimal-only scan: rules pattern-match
    // double/float to reject floats (CultureDecimalRule, CultureQuantityRule, ScaledQuantityRule,
    // CultureSideResolveRule) and read OA-date serials (DeclaredDateParsing, PartyFormatDateRule). New arithmetic
    // there goes through review, not this gate.
    internal static readonly string[] DecimalOnlyFolders =
    [
        "RecDiffy/Matching/",
        "RecDiffy/Consolidation/",
        "RecDiffy/Diff/",
        "RecDiffy/Results/",
    ];

    internal static readonly string[] CheckedDecimalFolders =
    [
        "RecDiffy/Consolidation/",
        "RecDiffy/Diff/",
        "RecDiffy/Matching/",
        "RecDiffy/Results/",
        "RecDiffy.RulePacks.Trading/",
    ];

    internal static readonly string[] CompositionExemptFiles =
    [
        "RecDiffy/Results/ResultBuilder.cs",
        "RecDiffy/Results/ResultModel.cs",
    ];

    internal static IReadOnlyList<(string RelativePath, string Content)> Sources(IReadOnlyList<string> roots) =>
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

    internal static string[] RawControlJoins(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => !file.RelativePath.EndsWith("/Delimited.cs", StringComparison.Ordinal))
            .Where(file => RawControlJoinTokens.Any(token => file.Content.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.RelativePath)
            .ToArray();

    internal static string[] SchemeSeparatorLeaks(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => file.RelativePath != "RecDiffy/Matching/MatchKeys.cs")
            .Where(file => file.Content.Contains("\u001e", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

    internal static string[] KeyTextLeaks(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => file.RelativePath != "RecDiffy/Matching/MatchKeys.cs")
            .Where(file =>
                file.Content.Contains("KeyText(", StringComparison.Ordinal)
                || file.Content.Contains("IdentifierKeyText(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

    internal static string[] FloatViolations(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => DecimalOnlyFolders.Any(prefix => file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(file => FloatOrDoubleToken.IsMatch(file.Content))
            .Select(file => file.RelativePath)
            .ToArray();

    internal static string[] CompositionLeaks(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => !CompositionExemptFiles.Contains(file.RelativePath, StringComparer.Ordinal))
            .Where(file => file.Content.Contains("Composition.", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToArray();

    internal static string[] UncheckedDecimalSums(IReadOnlyList<(string RelativePath, string Content)> sources) =>
        sources
            .Where(file => CheckedDecimalFolders.Any(prefix => file.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            .SelectMany(file => UncheckedSumCall.Matches(file.Content)
                .Where(match => !IsCountSum(file.Content, match.Index))
                .Select(match => file.RelativePath + ":" + LineOf(file.Content, match.Index)))
            .ToArray();

    private static bool IsCountSum(string content, int index) =>
        CountSumAllowance.IsMatch(content.AsSpan(index, Math.Min(64, content.Length - index)));

    private static int LineOf(string content, int index) => 1 + content.AsSpan(0, index).Count('\n');

    [Fact]
    public void R1_composite_keys_use_Delimited_Join_and_never_raw_string_Join_with_control_separators()
    {
        var sources = Sources(EngineRoots);
        Assert.NotEmpty(sources);

        Assert.Empty(RawControlJoins(sources));
        Assert.Empty(SchemeSeparatorLeaks(sources));
    }

    [Fact]
    public void R2_MatchKeys_KeyText_is_confined_to_MatchKeys_and_decimal_only_folders_never_use_float_or_double()
    {
        var sources = Sources(EngineRoots);

        Assert.Empty(KeyTextLeaks(sources));
        Assert.Empty(FloatViolations(sources));
    }

    [Fact]
    public void R4_Composition_flags_are_assigned_only_in_ResultBuilder()
    {
        var sources = Sources(EngineRoots);

        Assert.Empty(CompositionLeaks(sources));
    }

    [Fact]
    public void R5_economic_stages_use_CheckedDecimal_Sum_and_never_unchecked_Enumerable_Sum()
    {
        var sources = Sources(EngineRoots);

        Assert.Empty(UncheckedDecimalSums(sources));
    }
}
