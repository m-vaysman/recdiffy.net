namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture 2.0 cases that turn on signed cash, under the §9.2 profile of Fixture20OracleTests. Each fact states what the
// oracle says. A fact that fails today is skipped with the issue that holds the cause.
public class Fixture20SignedCashTests
{
    private static OracleComparison Only(IReadOnlySet<string> cases) =>
        new(Fixture20OracleTests.Outcome, OracleDatasets.Fixture20.Cases(), item => cases.Contains(item.Id) ? null : "another case");

    // Fixture 2.0 signs cash (§9.2), and trading.netCash explains only unsigned cash (§7.4). The profile names
    // trading.netCashSigned, which explains a signed BUY's net-cash delta (#433). Both facts failed with trading.netCash.
    [Fact]
    public void A_signed_BUYs_near_match_is_WithinTolerance()
    {
        OracleAssert.None(Only(OracleDefects.SignedNearMatches).Outcomes());
    }

    [Fact]
    public void A_signed_BUYs_break_is_rooted_where_the_oracle_says()
    {
        OracleAssert.None(Only(OracleDefects.SignedBreaks).BreakRoots(Fixture20OracleTests.Field));
    }

    // §3.3 lists fees.inferFromNet before cash.toPerspective, and with signed cash a dealer-view bond then gets a wrong fee.
    [Fact(Skip = OracleDefects.DealerViewFeeOrder)]
    public void A_dealer_view_bond_with_signed_cash_matches_exactly()
    {
        OracleAssert.None(Only(OracleDefects.DealerViewExact).Outcomes());
    }
}
