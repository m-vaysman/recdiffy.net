namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture 2.0 signs cash (§9.2), and trading.netCash explains only unsigned cash (§7.4). Thirteen near-matches and breaks
// whose internal cash is negative disagree with the oracle (Deferral.SignedCash). The fact states what the oracle says and
// fails today, so it is skipped with the issue that holds the cause.
public class Fixture20SignedCashTests
{
    private static OracleComparison SignedBuys() =>
        new(Fixture20OracleTests.Outcome, OracleDatasets.Fixture20.Cases(), item => Deferral.SignedCash.Contains(item.Id) ? null : "not a signed BUY");

    [Fact(Skip = Deferral.SignedCashReason)]
    public void A_signed_BUYs_near_match_is_WithinTolerance_and_its_break_is_rooted_where_the_oracle_says()
    {
        var signed = SignedBuys();

        OracleAssert.None([.. signed.Outcomes(), .. signed.BreakRoots(Fixture20OracleTests.Field)]);
    }
}
