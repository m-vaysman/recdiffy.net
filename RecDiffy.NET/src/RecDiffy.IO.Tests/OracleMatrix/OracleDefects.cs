namespace RecDiffy.IO.Tests.OracleMatrix;

// The oracle cases that the registered trading rules get wrong under the profiles of SonnetOracleTests and Fixture20OracleTests,
// by id, each with the issue that holds the cause. A check leaves out only the cases that fail it, so every other case is judged,
// and the skipped facts of SonnetFeeDefectTests and Fixture20SignedCashTests state what the oracle says about these.
internal static class OracleDefects
{
    // Sonnet's TWO_TO_ONE_NET_MATCH forwards: the street cash is the net of two legs, which is no principal, so §3.3's formula
    // gives a fee as large as the principal. §3.3 doesn't say what the street fee of a two-leg forward is.
    public const string TwoLegForwardFee = "#432: §3.3 derives a fee from quantity times rate, and a two-leg forward's net cash is not a principal: the spec doesn't say what its street fee is";

    public static IReadOnlySet<string> SonnetTwoLegForwards { get; } = new HashSet<string>(StringComparer.Ordinal) { "G0175", "G0176", "G0177", "G0178" };

    // §7.4 and Sonnet G0158: a swap's street fee can't be derived, so trading.netCash is to report its net-cash delta as a
    // derived Fees difference. The registered trading.netCash reports NetCash as the root.
    public const string SwapFeeRoot = "#431: §7.4 has trading.netCash blame Fees for a swap's net-cash delta, and the registered rule blames NetCash";

    public const string SwapFeeCase = "G0158";

    // Fixture 2.0 signs cash (§9.2: a BUY is negative). trading.netCash's identity is for unsigned cash, so it never explained
    // a signed BUY's net-cash delta, which has the opposite sign (#433). The fixture 2.0 profile now names
    // trading.netCashSigned, and these 13 cases agree with the oracle. Fixture20SignedCashTests keeps them as its own facts.
    //
    // The 9 near-matches that disagreed on outcome: every one a BUY whose internal net cash is negative.
    public static IReadOnlySet<string> SignedNearMatches { get; } = new HashSet<string>(StringComparer.Ordinal)
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
    };

    // The 4 breaks that agreed on outcome and disagreed on root: net cash was a root of its own beside the oracle's one.
    public static IReadOnlySet<string> SignedBreaks { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CASE-32BADE5493F96F8D",
        "CASE-AD2E22ECBC168DF4",
        "CASE-E56D4A056DC1D66E",
        "CASE-B3C99CCF3208F735",
    };

    // §3.3 lists fees.inferFromNet before cash.toPerspective, and fixture 2.0's profile keeps that order. With signed cash,
    // a dealer-view row's fee is then inferred from cash in the dealer's perspective. Running cash.toPerspective first clears these
    // 8 known-exact bonds. Whether §3.3's order is a rule is a question for the lead.
    public const string DealerViewFeeOrder = "#438: §3.3 puts fees.inferFromNet before cash.toPerspective, so a signed dealer-view row's fee is inferred from cash in the dealer's perspective";

    public static IReadOnlySet<string> DealerViewExact { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CASE-AE1244E3AC5AA966",
        "CASE-523F06781FEE6DA5",
        "CASE-5BB219E96A94A737",
        "CASE-E51685B951891FA1",
        "CASE-6999782C471D86C8",
        "CASE-17E509C29B5DFE92",
        "CASE-672431421C056083",
        "CASE-2AACF760345DCF0A",
    };

    // The swap price break CASE-BCC487E33A290260 also lists the coupon, which for a swap is the fixed rate. The engine
    // reports both as roots; the oracle blames the price alone, and no attribution ties the coupon to the price.
    public const string CouponIsTheRate = "Oracle note: a swap's coupon is its fixed rate, so the engine finds two roots where the oracle blames one";

    public const string SwapCouponCase = "CASE-BCC487E33A290260";
}
