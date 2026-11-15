namespace RecDiffy.IO.Tests.OracleMatrix;

// Sonnet groups the registered trading rules get wrong under the §9.1 profile of SonnetOracleTests. Each fact states what
// the oracle and the spec say and fails today, so it is skipped with the issue that holds the cause.
public class SonnetFeeDefectTests
{
    private static OracleComparison Only(params string[] groups) =>
        new(SonnetOracleTests.Outcome, OracleDatasets.Sonnet.Cases(), item => groups.Contains(item.Id, StringComparer.Ordinal) ? null : "another group");

    // §7.4: a swap's street fee can't be derived, so its +450.00 net-cash delta is a derived Fees difference, root Fees.
    [Fact(Skip = Deferral.SwapFeeRoot)]
    public void G0158_a_swaps_net_cash_delta_is_a_Fees_difference_and_Fees_is_the_root() =>
        OracleAssert.None(Only("G0158").BreakRoots(SonnetOracleTests.Field));

    // The oracle matches each two-leg forward exactly: the legs' net cash is the street cash and there is no fee to compare.
    [Fact(Skip = Deferral.TwoLegForwardFee)]
    public void G0175_to_G0178_a_two_leg_forward_matches_exactly() =>
        OracleAssert.None(Only("G0175", "G0176", "G0177", "G0178").Outcomes());
}
