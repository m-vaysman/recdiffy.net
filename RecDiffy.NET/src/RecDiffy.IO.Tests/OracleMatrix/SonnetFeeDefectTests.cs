namespace RecDiffy.IO.Tests.OracleMatrix;

// Sonnet groups the registered trading rules get wrong under the §9.1 profile of SonnetOracleTests. Each fact states what
// the oracle and the spec say and fails today, so it is skipped with the issue that holds the cause.
public class SonnetFeeDefectTests
{
    private static OracleComparison Only(params string[] groups) =>
        new(SonnetOracleTests.Outcome, OracleDatasets.Sonnet.Cases(), item => groups.Contains(item.Id, StringComparer.Ordinal) ? null : "another group");

    // §7.4: a swap's street fee can't be derived, so its +450.00 net-cash delta is a derived Fees difference of 450.00
    // (450.00 to 900.00), and Fees is the root.
    [Fact(Skip = OracleDefects.SwapFeeRoot)]
    public void G0158_a_swaps_net_cash_delta_is_a_Fees_difference_of_450_and_Fees_is_the_root()
    {
        var swap = Only(OracleDefects.SwapFeeCase);

        OracleAssert.None([.. swap.Outcomes(), .. swap.BreakRoots(SonnetOracleTests.Field), .. swap.Deltas(SonnetOracleTests.Field)]);
        var entry = SonnetOracleTests.Outcome.Entries.Single(candidate => candidate.Right.Any(row => row.RowKey == "UB-58460918"));
        var fees = Assert.Single(entry.Diffs, diff => diff.Field.Name == "Fees");
        Assert.Equal((450m, 900m, 450m), (fees.Left.Number, fees.Right.Number, fees.Delta.Number));
    }

    // The oracle matches each two-leg forward exactly: the legs' net cash is the street cash and there is no fee to compare.
    [Fact(Skip = OracleDefects.TwoLegForwardFee)]
    public void G0175_to_G0178_a_two_leg_forward_matches_exactly() =>
        OracleAssert.None(Only([.. OracleDefects.SonnetTwoLegForwards]).Outcomes());
}
