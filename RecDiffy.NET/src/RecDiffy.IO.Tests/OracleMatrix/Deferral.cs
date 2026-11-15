namespace RecDiffy.IO.Tests.OracleMatrix;

// Why a test class leaves a case out of its passing checks. Each class checks every case in a skipped test, so a deferred
// case is never unchecked: it waits on the rule, the defect or the lead's decision its reason names.
internal static class Deferral
{
    // The four secondary datasets' profiles (Qwen, DeepSeek, Opus, v2) are stand-ins that don't use these rules.
    public const string DirtyToClean = "this profile doesn't use price.dirtyToClean: a dirty street price is compared as written";

    public const string InferFees = "this profile doesn't use fees.inferFromNet: fees are not compared, so trading.netCash can't settle net cash as the oracle does";

    public const string CashToPerspective = "this profile doesn't use cash.toPerspective: a dealer-view row's cash is not negated";

    // Sonnet's TWO_TO_ONE_NET_MATCH forwards: the street cash is the net of two legs, which is no principal, so §3.3's
    // formula gives a fee as large as the principal. §3.3 doesn't say what the street fee of a two-leg forward is.
    public const string TwoLegForwardFee = "#432: §3.3 derives a fee from quantity times rate, and a two-leg forward's net cash is not a principal: the spec doesn't say what its street fee is";

    // §7.4 and Sonnet G0158: a swap's street fee can't be derived, so trading.netCash is to report its net-cash delta as a
    // derived Fees difference. The registered trading.netCash reports NetCash as the root.
    public const string SwapFeeRoot = "#431: §7.4 has trading.netCash blame Fees for a swap's net-cash delta, and the registered rule blames NetCash";

    // Fixture 2.0 signs cash (§9.2: a BUY is negative). trading.netCash's identity adds each root's delta to the net-cash delta
    // as an unsigned amount, so a signed BUY's net-cash delta, which has the opposite sign, is not explained.
    public const string SignedCash = "#433: fixture 2.0's cash is signed, and trading.netCash explains only unsigned cash (§7.4): a BUY's net-cash delta is not explained";

    // True when any of the case's right rows holds the value in the column.
    public static bool RightHas(OracleDataset dataset, OracleCase item, string column, string value) =>
        item.Right.Any(key => dataset.RightRow(key) is { } row && SheetRows.Text(row.GetValueOrDefault(column)) == value);

    // True when any of the case's left rows holds the value in the column.
    public static bool LeftHas(OracleDataset dataset, OracleCase item, string column, string value) =>
        item.Left.Any(key => dataset.LeftRow(key) is { } row && SheetRows.Text(row.GetValueOrDefault(column)) == value);

    // True when any of the case's left rows has a net settlement amount below zero.
    public static bool LeftPaysCash(OracleDataset dataset, OracleCase item) =>
        item.Left.Any(key => dataset.LeftRow(key) is { } row && row.GetValueOrDefault("Net_Settlement_Amount") is decimal net && net < 0m);
}
