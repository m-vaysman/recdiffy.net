using System.Reflection;
using Xunit.Sdk;

namespace RecDiffy.IO.Tests.OracleMatrix;

// The deferral ledger of the acceptance matrix (#39, #406): every skipped fact of the matrix, by class and method. A skip
// added or removed changes this list, so a fact can't leave the matrix, or join it, unseen. Each skip's reason is in its
// own attribute; the ones that wait on a decision or a defect name its issue (#431, #432, #438).
// The scan reads a skip wherever xunit does: on an instance or a static method, on a fact or a theory, on a row of theory
// data, and in a nested public class. A reason that is empty or only spaces is no reason, and an empty skip is not a skip.
public class OracleMatrixLedgerTests
{
    private static readonly string[] Skipped =
    [
        "DeepSeekOracleTests.AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to",
        "DeepSeekOracleTests.AC4_each_chain_survivor_is_the_oracles_active_report_and_the_rest_are_suppressed",
        "Fixture20OracleTests.Every_case_agrees_with_the_oracle_on_every_criterion",
        "Fixture20SignedCashTests.A_dealer_view_bond_with_signed_cash_matches_exactly",
        "FixtureV2OracleTests.AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to",
        "OpusOracleTests.AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to",
        "OpusOracleTests.AC4_each_chain_survivor_is_the_oracles_active_report_and_the_rest_are_suppressed",
        "QwenOracleTests.AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to",
        "SonnetFeeDefectTests.G0158_a_swaps_net_cash_delta_is_a_Fees_difference_of_450_and_Fees_is_the_root",
        "SonnetFeeDefectTests.G0175_to_G0178_a_two_leg_forward_matches_exactly",
        "SonnetOracleTests.AC2_the_run_counts_are_145_exact_18_within_tolerance_20_break_85_left_only_and_7_right_only",
        "SonnetOracleTests.Every_group_agrees_with_the_oracle_on_every_criterion",
    ];

    // Fixture v1 is fully accepted, and the per-field checks of fixture 2.0 have no skip.
    private static readonly string[] SupportedClasses = ["FixtureV1OracleTests", "FixtureV1DeltaTests", "Fixture20FieldDifferenceTests"];

    private sealed record Skip(string Name, string Reason);

    private static string Qualified(Type type) => type.FullName![(type.Namespace!.Length + 1)..];

    // Every skip on a public method of the types: its own skip, and one for each row of theory data that carries one.
    private static IReadOnlyList<Skip> Skips(IEnumerable<Type> types) =>
    [
        .. types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => (method.GetCustomAttributes<FactAttribute>().Select(fact => fact.Skip)
                    .Concat(method.GetCustomAttributes<DataAttribute>().Select(data => data.Skip)))
                .Where(reason => string.IsNullOrEmpty(reason) is false)
                .Select(reason => new Skip($"{Qualified(type)}.{method.Name}", reason!)))),
    ];

    private static IEnumerable<Type> MatrixTypes() =>
        typeof(OracleMatrixLedgerTests).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(OracleMatrixLedgerTests).Namespace && (type.IsPublic || type.IsNestedPublic));

    [Fact]
    public void The_skipped_facts_of_the_matrix_are_exactly_the_ledger()
    {
        var skipped = Skips(MatrixTypes()).Select(skip => skip.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(Skipped, skipped, StringComparer.Ordinal);
    }

    [Fact]
    public void Every_skip_says_why()
    {
        var silent = Skips(MatrixTypes()).Where(skip => skip.Reason.Trim().Length < 20).Select(skip => skip.Name).ToArray();

        Assert.Empty(silent);
    }

    [Fact]
    public void Fixture_v1_and_the_field_difference_checks_have_no_skipped_fact()
    {
        var skipped = Skips(MatrixTypes()).Where(skip => SupportedClasses.Contains(skip.Name.Split('.')[0], StringComparer.Ordinal)).ToArray();

        Assert.Empty(skipped);
    }

    // The scan is tried on a type that skips in every way the matrix could: a static fact, a theory, a row of theory data, and
    // a reason of spaces. The type is not public, so the matrix scan does not see it, and xunit does not run it.
    [Fact]
    public void The_scan_finds_a_skip_on_a_static_method_a_theory_a_data_row_and_a_blank_reason()
    {
        var found = Skips([typeof(Samples)]).Select(skip => $"{skip.Name}:{skip.Reason.Trim().Length.ToInvariant()}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "OracleMatrixLedgerTests+Samples.ABlank:0",
                "OracleMatrixLedgerTests+Samples.AStaticFact:3",
                "OracleMatrixLedgerTests+Samples.ATheory:6",
                "OracleMatrixLedgerTests+Samples.ATheoryRow:7",
            ],
            found,
            StringComparer.Ordinal);
    }

#pragma warning disable xUnit1000, xUnit1013, CA1812, S1144, CA1822, MA0182
    private sealed class Samples
    {
        [Fact(Skip = "why")]
        public static void AStaticFact()
        {
        }

        [Theory(Skip = "theory")]
        [InlineData(1)]
        public void ATheory(int value) => Assert.True(value > 0);

        [Theory]
        [InlineData(1, Skip = "one row")]
        public void ATheoryRow(int value) => Assert.True(value > 0);

        [Fact(Skip = "   ")]
        public void ABlank()
        {
        }
    }
#pragma warning restore xUnit1000, xUnit1013, CA1812, S1144, CA1822, MA0182
}
