namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture 2.0 signs cash (§9.2), and trading.netCash explains only unsigned cash (§7.4). Every case whose internal cash is
// negative and that is a near-match or a break disagrees with the oracle, bar the swap coupon (an oracle note). The facts
// state what the oracle says and fail today, so they are skipped with the issue that holds the cause.
public class Fixture20SignedCashTests
{
    private const string SwapCoupon = "CASE-BCC487E33A290260";

    private static OracleComparison SignedBuys() =>
        new(
            Fixture20OracleTests.Outcome,
            OracleDatasets.Fixture20.Cases(),
            item => item.Id != SwapCoupon && item.Kind is (CaseKind.WithinTolerance or CaseKind.Break) && Deferral.LeftPaysCash(OracleDatasets.Fixture20, item) ? null : "not a signed BUY");

    [Fact(Skip = Deferral.SignedCash)]
    public void A_signed_BUYs_near_match_is_WithinTolerance_and_its_break_is_rooted_where_the_oracle_says()
    {
        var signed = SignedBuys();

        OracleAssert.None([.. signed.Outcomes(), .. signed.BreakRoots(Fixture20OracleTests.Field)]);
    }
}
