namespace RecDiffy.Tests;

// Kill-switch tests for the #331 source gates: each detector must trip on a planted violation, so a gate edit
// that silently stops detecting fails here instead of passing vacuously.
public class ArchitectureInvariantGateDetectorTests
{
    [Fact]
    public void R1_detector_flags_raw_control_separator_joins_only_outside_Delimited()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Matching/GroupIndex.cs", "var key = string.Join('\u001f', a, b);")];
        Assert.Equal(["RecDiffy/Matching/GroupIndex.cs"], ArchitectureInvariantGateTests.RawControlJoins(planted));

        (string RelativePath, string Content)[] joined = [("RecDiffy/Matching/GroupIndex.cs", "var key = Delimited.Join('\u001f', a, b);")];
        Assert.Empty(ArchitectureInvariantGateTests.RawControlJoins(joined));

        (string RelativePath, string Content)[] guarded = [("RecDiffy/Matching/Delimited.cs", "var key = string.Join('\u001f', a, b);")];
        Assert.Empty(ArchitectureInvariantGateTests.RawControlJoins(guarded));
    }

    [Fact]
    public void R1_detector_flags_a_scheme_separator_outside_MatchKeys()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Results/ResultBuilder.cs", "var key = a + \"\u001e\" + b;")];
        Assert.Equal(["RecDiffy/Results/ResultBuilder.cs"], ArchitectureInvariantGateTests.SchemeSeparatorLeaks(planted));

        (string RelativePath, string Content)[] guarded = [("RecDiffy/Matching/MatchKeys.cs", "var key = a + \"\u001e\" + b;")];
        Assert.Empty(ArchitectureInvariantGateTests.SchemeSeparatorLeaks(guarded));
    }

    [Fact]
    public void R2_detector_flags_KeyText_only_outside_MatchKeys()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Results/ResultBuilder.cs", "var text = MatchKeys.KeyText(field, value);")];
        Assert.Equal(["RecDiffy/Results/ResultBuilder.cs"], ArchitectureInvariantGateTests.KeyTextLeaks(planted));

        (string RelativePath, string Content)[] guarded = [("RecDiffy/Matching/MatchKeys.cs", "var text = MatchKeys.KeyText(field, value);")];
        Assert.Empty(ArchitectureInvariantGateTests.KeyTextLeaks(guarded));
    }

    [Fact]
    public void R2_detector_flags_float_only_in_decimal_only_folders()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Results/SideBySide.cs", "double tolerance = 0.5;")];
        Assert.Equal(["RecDiffy/Results/SideBySide.cs"], ArchitectureInvariantGateTests.FloatViolations(planted));

        (string RelativePath, string Content)[] rulesFolder = [("RecDiffy/Rules/CultureDecimalRule.cs", "double tolerance = 0.5;")];
        Assert.Empty(ArchitectureInvariantGateTests.FloatViolations(rulesFolder));

        (string RelativePath, string Content)[] tradingFolder = [("RecDiffy.RulePacks.Trading/NetCashAttribution.cs", "float share = 0.5f;")];
        Assert.Empty(ArchitectureInvariantGateTests.FloatViolations(tradingFolder));
    }

    [Fact]
    public void R4_detector_flags_Composition_assignment_only_outside_the_exempt_files()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Consolidation/Lifecycle.cs", "var c = Composition.OneToOne | Composition.Package;")];
        Assert.Equal(["RecDiffy/Consolidation/Lifecycle.cs"], ArchitectureInvariantGateTests.CompositionLeaks(planted));

        (string RelativePath, string Content)[] builder = [("RecDiffy/Results/ResultBuilder.cs", "var c = Composition.OneToOne | Composition.Package;")];
        Assert.Empty(ArchitectureInvariantGateTests.CompositionLeaks(builder));

        (string RelativePath, string Content)[] model = [("RecDiffy/Results/ResultModel.cs", "var c = Composition.OneToOne | Composition.Package;")];
        Assert.Empty(ArchitectureInvariantGateTests.CompositionLeaks(model));
    }

    [Fact]
    public void R5_detector_flags_unchecked_Sum_but_not_CheckedDecimal_or_the_count_sum_allowance()
    {
        (string RelativePath, string Content)[] planted = [("RecDiffy/Results/SideBySide.cs", "var total = rows.Sum(r => r.Amount);")];
        Assert.Equal(["RecDiffy/Results/SideBySide.cs:1"], ArchitectureInvariantGateTests.UncheckedDecimalSums(planted));

        (string RelativePath, string Content)[] checkedSum = [("RecDiffy/Results/SideBySide.cs", "var total = CheckedDecimal.Sum(rows);")];
        Assert.Empty(ArchitectureInvariantGateTests.UncheckedDecimalSums(checkedSum));

        (string RelativePath, string Content)[] countSum = [("RecDiffy/Results/ResultBuilder.cs", "var leftCount = group.Left.Sum(row => row.Packaged ? row.Members.Count : 1);")];
        Assert.Empty(ArchitectureInvariantGateTests.UncheckedDecimalSums(countSum));

        (string RelativePath, string Content)[] otherSumInBuilder = [("RecDiffy/Results/ResultBuilder.cs", "var total = rows.Sum(r => r.Amount);")];
        Assert.Equal(["RecDiffy/Results/ResultBuilder.cs:1"], ArchitectureInvariantGateTests.UncheckedDecimalSums(otherSumInBuilder));

        (string RelativePath, string Content)[] unscannedFolder = [("RecDiffy/Rules/CultureDecimalRule.cs", "var total = rows.Sum(r => r.Amount);")];
        Assert.Empty(ArchitectureInvariantGateTests.UncheckedDecimalSums(unscannedFolder));
    }
}
