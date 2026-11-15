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
    public const string SignedCashReason = "#433: fixture 2.0's cash is signed, and trading.netCash explains only unsigned cash (§7.4): a BUY's net-cash delta is not explained";

    // True when any of the case's right rows holds the value in the column.
    public static bool RightHas(OracleDataset dataset, OracleCase item, string column, string value) =>
        item.Right.Any(key => dataset.RightRow(key) is { } row && SheetRows.Text(row.GetValueOrDefault(column)) == value);

    // True when any of the case's left rows holds the value in the column.
    public static bool LeftHas(OracleDataset dataset, OracleCase item, string column, string value) =>
        item.Left.Any(key => dataset.LeftRow(key) is { } row && SheetRows.Text(row.GetValueOrDefault(column)) == value);

    // The fixture 2.0 cases that disagree with the oracle because their cash is signed (#433): 9 near-matches and 4 breaks,
    // every one a BUY whose internal net cash is negative. Two other signed breaks agree, so they are judged.
    public static IReadOnlySet<string> SignedCash { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CASE-C23BAF9FAE2304F9",
        "CASE-B30F53E282BBF4EB",
        "CASE-5815B5E725A14A99",
        "CASE-464C3900AB52BBF9",
        "CASE-CD0C3AD6F2EDA44E",
        "CASE-7C59E6C3CA837D6A",
        "CASE-AD6C6A30F2D0A8AD",
        "CASE-91DE8E3917A9B141",
        "CASE-09CAFF9B562A8642",
        "CASE-32BADE5493F96F8D",
        "CASE-AD2E22ECBC168DF4",
        "CASE-E56D4A056DC1D66E",
        "CASE-B3C99CCF3208F735",
    };
}
